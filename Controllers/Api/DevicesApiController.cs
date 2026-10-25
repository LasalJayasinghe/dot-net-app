using System.Security.Claims;
using dotnetApp.Domain.Entities;
using dotnetApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

/// <summary>
/// Where the mobile app parks its Expo push token so the alert job can reach it.
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DevicesApiController> _logger;

    public DevicesApiController(AppDbContext dbContext, ILogger<DevicesApiController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Registers (or re-registers) this installation. The app calls it on every
    /// launch, because Expo may hand out a new token after a reinstall or a
    /// restored backup, and an unused old token must not keep receiving alerts.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var token = (dto.Token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Push token is required" });

        // Anything else is a client bug or a stale FCM token, and Expo would
        // reject it anyway - fail loudly here instead of at send time.
        if (!token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) &&
            !token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
            return BadRequest(new { message = "Not a valid Expo push token" });

        var existing = await _dbContext.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);

        if (existing == null)
        {
            _dbContext.DeviceTokens.Add(new DeviceToken
            {
                UserId = userId,
                Token = token,
                Platform = Truncate(dto.Platform, 20),
                DeviceName = Truncate(dto.DeviceName, 100),
            });
        }
        else
        {
            // A shared or resold handset: the token follows whoever is signed in,
            // so the previous owner stops receiving this device's alerts.
            existing.UserId = userId;
            existing.Platform = Truncate(dto.Platform, 20) ?? existing.Platform;
            existing.DeviceName = Truncate(dto.DeviceName, 100) ?? existing.DeviceName;
            existing.IsActive = true;
            existing.LastSeenAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Registered push token for user {UserId}.", userId);

        return Ok(new { registered = true });
    }

    /// <summary>Called on sign-out, so the next user of the handset inherits nothing.</summary>
    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var token = (dto.Token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Push token is required" });

        var row = await _dbContext.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token && d.UserId == userId);

        if (row != null)
        {
            _dbContext.DeviceTokens.Remove(row);
            await _dbContext.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>The devices currently signed in for this user.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var devices = await _dbContext.DeviceTokens
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => new
            {
                d.Id,
                d.Platform,
                d.DeviceName,
                d.IsActive,
                d.LastSeenAt,
            })
            .ToListAsync();

        return Ok(devices);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    public class RegisterDeviceDto
    {
        public string? Token { get; set; }
        public string? Platform { get; set; }
        public string? DeviceName { get; set; }
    }

    public class UnregisterDeviceDto
    {
        public string? Token { get; set; }
    }
}
