using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileApiController : ControllerBase
{
    private readonly ProfileRepository _profileRepository;

    public ProfileApiController(ProfileRepository profileRepository)
    {
        _profileRepository = profileRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var profile = await _profileRepository.GetProfileByUserIdAsync(userId, cancellationToken);

        if (profile == null)
            return NotFound(new { message = "Profile not found" });

        return Ok(new ProfileDto
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Bio = profile.Bio,
            TelegramId = profile.TelegramId,
            Username = profile.User?.UserName,
            Email = profile.User?.Email
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto profileDto, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var updatedProfile = await _profileRepository.UpdateProfileAsync(userId, profileDto, cancellationToken);

        if (updatedProfile == null)
            return NotFound(new { message = "Profile not found" });

        return Ok(new ProfileDto
        {
            FirstName = updatedProfile.FirstName,
            LastName = updatedProfile.LastName,
            Bio = updatedProfile.Bio,
            TelegramId = updatedProfile.TelegramId,
            Username = updatedProfile.User?.UserName,
            Email = updatedProfile.User?.Email
        });
    }

    [HttpPut]
    [Route("telegram")]
    public async Task<IActionResult> UpdateTelegramId([FromBody] UpdateTelegramIdDto telegramIdDto, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var updatedProfile = await _profileRepository.UpdateTelegramIdAsync(userId, telegramIdDto, cancellationToken);

        if (updatedProfile == null)
            return NotFound(new { message = "Profile not found" });

        return Ok(new ProfileDto
        {
            FirstName = updatedProfile.FirstName,
            LastName = updatedProfile.LastName,
            Bio = updatedProfile.Bio,
            TelegramId = updatedProfile.TelegramId,
            Username = updatedProfile.User?.UserName,
            Email = updatedProfile.User?.Email
        });
    }
}