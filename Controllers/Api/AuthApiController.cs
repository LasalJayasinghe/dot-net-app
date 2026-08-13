using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Google.Apis.Auth;

using dotnetApp.Application.Dtos;
using dotnetApp.Application.Interface;
using dotnetApp.Infrastructure.Data;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TokenService _tokenService;
    private readonly ProfileRepository _profileRepository;
    private readonly AppDbContext _context;
    private readonly IBrevoEmailService _brevoEmailService;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        ProfileRepository profileRepository,
        AppDbContext context,
        IBrevoEmailService brevoEmailService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _profileRepository = profileRepository;
        _context = context;
        _brevoEmailService = brevoEmailService;
    }


    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto model)
    {
        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null)
            return Unauthorized("Invalid credentials");

        var profile = await _profileRepository.GetProfileByUserIdAsync(user.Id, CancellationToken.None);
        if (profile == null)
            return Unauthorized("Profile not found for user");

        var passwordValid = await _userManager.CheckPasswordAsync(user, model.Password);

        if (!passwordValid)
            return Unauthorized("Invalid credentials");

        var token = await _tokenService.CreateTokenAsync(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(new
        {
            token = token,
            refreshToken = refreshToken,
            firstName = profile.FirstName,
            lastName = profile.LastName,
        });
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.IdToken))
            return BadRequest("ID token is missing.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            // You can optionally pass validation settings here, including the specific Client ID to validate against.
            // For now, we'll use the default validation which checks the signature against Google's public keys.
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken);
        }
        catch (InvalidJwtException)
        {
            return Unauthorized("Invalid Google ID token.");
        }

        if (string.IsNullOrEmpty(payload.Email))
            return BadRequest("Google account has no email address.");

        // Check if user exists
        var user = await _userManager.FindByEmailAsync(payload.Email);
        Profile profile = null;

        if (user == null)
        {
            // Create user
            user = new ApplicationUser
            {
                UserName = payload.Email,
                Email = payload.Email,
                EmailConfirmed = true
            };
            
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return BadRequest("Failed to create user account.");

            // Assign role
            await _userManager.AddToRoleAsync(user, "User");

            // Create profile
            profile = new Profile
            {
                UserId = user.Id,
                FirstName = payload.GivenName ?? "User",
                LastName = payload.FamilyName ?? "",
                Bio = ""
            };
            
            await _profileRepository.AddProfileAsync(profile);
        }
        else
        {
            profile = await _profileRepository.GetProfileByUserIdAsync(user.Id, CancellationToken.None);
        }

        var token = await _tokenService.CreateTokenAsync(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(user);

        return Ok(new
        {
            token = token,
            refreshToken = refreshToken,
            firstName = profile?.FirstName ?? payload.GivenName ?? "User",
            lastName = profile?.LastName ?? payload.FamilyName ?? ""
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unauthorized();

        var token = await _userManager.Users.FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken);

        if (token == null || token.RefreshTokenExpiry < DateTime.UtcNow)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(token.Id);
        if (user == null)
            return Unauthorized();

        var newAccessToken = await _tokenService.CreateTokenAsync(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(updateResult.Errors);

        return Ok(new { token = newAccessToken, accessToken = newAccessToken, refreshToken = newRefreshToken });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
            return NotFound();

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _userManager.UpdateAsync(user);

        return Ok();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
            return NotFound(new { message = "User not found" });

        var result = await _userManager.ChangePasswordAsync(user, changePasswordDto.CurrentPassword, changePasswordDto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { message = "Current password is incorrect" });

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await _userManager.UpdateAsync(user);

        return Ok(new { message = "Password changed successfully" });
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        var email = request.Email.Trim().ToLower();
        var purpose = request.Purpose ?? "SignUp";

        // Purpose Validation
        if (purpose.Equals("SignUp", StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
                return BadRequest(new { message = "An account with this email already exists. Please sign in." });
        }
        else if (purpose.Equals("ForgotPassword", StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser == null)
                return BadRequest(new { message = "No registered account found with this email." });
        }
        else
        {
            return BadRequest(new { message = "Invalid OTP purpose." });
        }

        // Invalidate previous active OTPs for this email and purpose
        var oldOtps = await _context.OtpRecords
            .Where(o => o.Email == email && o.Purpose == purpose && !o.IsUsed)
            .ToListAsync();

        foreach (var old in oldOtps)
        {
            old.IsUsed = true;
        }

        // Generate 6-digit OTP code
        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var otpRecord = new OtpRecord
        {
            Email = email,
            Code = code,
            Purpose = purpose,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false
        };

        _context.OtpRecords.Add(otpRecord);
        await _context.SaveChangesAsync();

        // Dispatch Email via Brevo
        var emailSent = await _brevoEmailService.SendOtpEmailAsync(email, code, purpose);
        if (!emailSent)
        {
            return StatusCode(500, new { message = "Failed to send verification email. Please try again." });
        }

        return Ok(new { message = "Verification code sent to your email." });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterWithOtpDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required." });

        var email = request.Email.Trim().ToLower();
        var otpCode = request.OtpCode?.Trim();

        if (string.IsNullOrWhiteSpace(otpCode))
            return BadRequest(new { message = "Verification code is required." });

        // Validate OTP from Database
        var validOtp = await _context.OtpRecords
            .Where(o => o.Email == email && o.Purpose == "SignUp" && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow && o.Code == otpCode)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (validOtp == null)
            return BadRequest(new { message = "Invalid or expired verification code." });

        // Mark OTP as used
        validOtp.IsUsed = true;

        // Check if user already exists
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
            return BadRequest(new { message = "An account with this email already exists." });

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var firstErr = createResult.Errors.FirstOrDefault()?.Description ?? "Failed to create user account.";
            return BadRequest(new { message = firstErr });
        }

        await _userManager.AddToRoleAsync(user, "User");

        var profile = new Profile
        {
            UserId = user.Id,
            FirstName = !string.IsNullOrWhiteSpace(request.FirstName) ? request.FirstName : "Trader",
            LastName = !string.IsNullOrWhiteSpace(request.LastName) ? request.LastName : "",
            Bio = ""
        };

        await _profileRepository.AddProfileAsync(profile);
        await _context.SaveChangesAsync();

        var token = await _tokenService.CreateTokenAsync(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(user);

        return Ok(new
        {
            token = token,
            refreshToken = refreshToken,
            firstName = profile.FirstName,
            lastName = profile.LastName
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordWithOtpDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { message = "Email and new password are required." });

        var email = request.Email.Trim().ToLower();
        var otpCode = request.OtpCode?.Trim();

        if (string.IsNullOrWhiteSpace(otpCode))
            return BadRequest(new { message = "Verification code is required." });

        // Validate OTP from Database
        var validOtp = await _context.OtpRecords
            .Where(o => o.Email == email && o.Purpose == "ForgotPassword" && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow && o.Code == otpCode)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (validOtp == null)
            return BadRequest(new { message = "Invalid or expired verification code." });

        // Mark OTP as used
        validOtp.IsUsed = true;
        await _context.SaveChangesAsync();

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
            return BadRequest(new { message = "User account not found." });

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!resetResult.Succeeded)
        {
            var firstErr = resetResult.Errors.FirstOrDefault()?.Description ?? "Failed to reset password.";
            return BadRequest(new { message = firstErr });
        }

        return Ok(new { message = "Password reset successfully. You can now sign in with your new password." });
    }
}


public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = string.Empty;
}