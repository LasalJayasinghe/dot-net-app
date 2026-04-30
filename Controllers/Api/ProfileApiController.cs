using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        var userId = User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (userId == null) return Unauthorized();

        var profile = await _profileRepository.GetProfileByUserIdAsync(userId , cancellationToken);
        if (profile == null) return NotFound();

        return Ok(new ProfileDto
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Bio = profile.Bio
        });
        
    }
}