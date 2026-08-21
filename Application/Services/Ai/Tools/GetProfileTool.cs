using System.Text.Json;
using dotnetApp.Infrastructure.Repositories;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetProfileTool : IMcpTool
{
    private readonly ProfileRepository _profileRepository;

    public GetProfileTool(ProfileRepository profileRepository)
    {
        _profileRepository = profileRepository;
    }

    public string Name => "get_profile";

    public string Description => "Gets the current logged-in user's profile information, including their name, bio, and connected Telegram ID.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    public async Task<string> ExecuteAsync(JsonElement parameters, string userId)
    {
        try
        {
            var profile = await _profileRepository.GetProfileByUserIdAsync(userId, CancellationToken.None);
            if (profile == null) return "Profile not found.";

            var profileDto = new 
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Bio = profile.Bio,
                TelegramId = profile.TelegramId,
                Username = profile.User?.UserName,
                Email = profile.User?.Email
            };

            return JsonSerializer.Serialize(profileDto);
        }
        catch (Exception ex)
        {
            return $"Error fetching profile: {ex.Message}";
        }
    }
}
