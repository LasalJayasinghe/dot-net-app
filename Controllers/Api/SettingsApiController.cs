using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using dotnetApp.Infrastructure.Data;
using dotnetApp.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BinanceP2pService _p2pService;
    public static readonly Dictionary<string, UserSettingsDto> Store = new();

    public SettingsApiController(AppDbContext db, BinanceP2pService p2pService)
    {
        _db = db;
        _p2pService = p2pService;
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

        // If rate is missing or older than 24 hours (once a day), auto refresh from Binance P2P
        if (usdtToLkr == null || (DateTime.UtcNow - usdtToLkr.LastUpdated).TotalHours > 24)
        {
            var (rateUsdtLkr, rateLkrUsdt) = await _p2pService.FetchAndUpdateUsdtLkrRateAsync();
            settings.UsdtToLkrRate = rateUsdtLkr;
            settings.LkrToUsdtRate = rateLkrUsdt;
        }
        else
        {
            settings.UsdtToLkrRate = usdtToLkr.Rate;
            settings.LkrToUsdtRate = lkrToUsdt?.Rate ?? (1m / usdtToLkr.Rate);
        }

        return Ok(settings);
    }

    [HttpPost("sync-exchange-rate")]
    public async Task<IActionResult> SyncExchangeRate()
    {
        var (usdtToLkr, lkrToUsdt) = await _p2pService.FetchAndUpdateUsdtLkrRateAsync();
        return Ok(new
        {
            message = "USDT/LKR exchange rate successfully synced from Binance P2P API.",
            usdtToLkrRate = usdtToLkr,
            lkrToUsdtRate = lkrToUsdt,
            lastUpdated = DateTime.UtcNow
        });
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
