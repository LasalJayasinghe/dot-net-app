using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Google.Apis.Auth;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TokenService _tokenService;
    private readonly ProfileRepository _profileRepository;

    public AuthController(UserManager<ApplicationUser> userManager, TokenService tokenService, ProfileRepository profileRepository)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _profileRepository = profileRepository;
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
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = string.Empty;
}