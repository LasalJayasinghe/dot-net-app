using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] string refreshToken)
    {
        var token = await _userManager.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);

        if (token == null || token.RefreshTokenExpiry < DateTime.UtcNow)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(token.Id);

        var newAccessToken = await _tokenService.CreateTokenAsync(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken();


        return Ok(new { accessToken = newAccessToken, refreshToken = newRefreshToken });
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

    [HttpPost]
    [Route("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
            return NotFound(new { message = "User not found" });

        var result = await _userManager.ChangePasswordAsync(user, changePasswordDto.CurrentPassword, changePasswordDto.NewPassword);
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return BadRequest(new { message = "Current password is incorrect" });

        return Ok(new { message = "Password changed successfully" });
    }
}