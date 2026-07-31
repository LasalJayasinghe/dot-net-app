using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsApiController : ControllerBase
{
    private static readonly Dictionary<string, UserSettingsDto> _store = new();

    [HttpGet]
    public IActionResult Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        if (!_store.TryGetValue(userId, out var settings))
        {
            settings = new UserSettingsDto
            {
                EmailNotifications = true,
                PriceAlerts = true,
                TwoFactorAuthentication = false,
            };
            _store[userId] = settings;
        }

        return Ok(settings);
    }

    [HttpPut]
    public IActionResult Update([FromBody] UserSettingsDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var normalized = new UserSettingsDto
        {
            EmailNotifications = input.EmailNotifications,
            PriceAlerts = input.PriceAlerts,
            TwoFactorAuthentication = input.TwoFactorAuthentication,
        };

        _store[userId] = normalized;
        return Ok(normalized);
    }

    public class UserSettingsDto
    {
        public bool EmailNotifications { get; set; }
        public bool PriceAlerts { get; set; }
        public bool TwoFactorAuthentication { get; set; }
    }
}
