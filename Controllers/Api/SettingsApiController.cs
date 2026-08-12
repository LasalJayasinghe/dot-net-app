using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    public static readonly Dictionary<string, UserSettingsDto> Store = new();

    public SettingsApiController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        if (!Store.TryGetValue(userId, out var settings))
        {
            settings = new UserSettingsDto
            {
                EmailNotifications = true,
                PriceAlerts = true,
                TwoFactorAuthentication = false,
            };
            Store[userId] = settings;
        }

        var usdtToLkr = await _db.CurrencyExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == "USDT" && r.ToCurrency == "LKR");
        var lkrToUsdt = await _db.CurrencyExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == "LKR" && r.ToCurrency == "USDT");

        settings.UsdtToLkrRate = usdtToLkr?.Rate ?? 300m;
        settings.LkrToUsdtRate = lkrToUsdt?.Rate ?? 0.0033m;

        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UserSettingsDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var normalized = new UserSettingsDto
        {
            EmailNotifications = input.EmailNotifications,
            PriceAlerts = input.PriceAlerts,
            TwoFactorAuthentication = input.TwoFactorAuthentication,
            UsdtToLkrRate = input.UsdtToLkrRate,
            LkrToUsdtRate = input.LkrToUsdtRate
        };

        Store[userId] = normalized;

        // Update DB
        var usdtToLkr = await _db.CurrencyExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == "USDT" && r.ToCurrency == "LKR");
        if (usdtToLkr == null) {
            _db.CurrencyExchangeRates.Add(new CurrencyExchangeRate { FromCurrency = "USDT", ToCurrency = "LKR", Rate = input.UsdtToLkrRate, LastUpdated = DateTime.UtcNow });
        } else {
            usdtToLkr.Rate = input.UsdtToLkrRate;
            usdtToLkr.LastUpdated = DateTime.UtcNow;
        }

        var lkrToUsdt = await _db.CurrencyExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == "LKR" && r.ToCurrency == "USDT");
        if (lkrToUsdt == null) {
            _db.CurrencyExchangeRates.Add(new CurrencyExchangeRate { FromCurrency = "LKR", ToCurrency = "USDT", Rate = input.LkrToUsdtRate, LastUpdated = DateTime.UtcNow });
        } else {
            lkrToUsdt.Rate = input.LkrToUsdtRate;
            lkrToUsdt.LastUpdated = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        return Ok(normalized);
    }

    public class UserSettingsDto
    {
        public bool EmailNotifications { get; set; }
        public bool PriceAlerts { get; set; }
        public bool TwoFactorAuthentication { get; set; }
        public decimal UsdtToLkrRate { get; set; }
        public decimal LkrToUsdtRate { get; set; }
    }
}
