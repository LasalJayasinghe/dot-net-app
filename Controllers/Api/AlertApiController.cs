using dotnetApp.Application.Services;
using dotnetApp.Application.ViewModels.Alerts;
using dotnetApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly MarketPriceResolver _prices;

    public AlertApiController(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        MarketPriceResolver prices)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _prices = prices;
    }

    private static object ToDto(Alert alert, decimal? currentPrice = null)
    {
        return new
        {
            id = alert.Id,
            symbol = alert.Symbol,
            targetPrice = alert.TargetPrice,
            currentPrice,
            isAbove = alert.IsAbove,
            isActive = alert.IsActive,
            fireOnce = alert.FireOnce,
            lastTriggeredAt = alert.LastTriggeredAt,
            createdAt = alert.CreatedAt,
            // "Stock" | "Index" | "Crypto" | "Fx" (index/crypto/FX symbols: ASPI, SNPSL20, BTCUSDT, SOLUSDT, USDTLKR)
            assetType = MarketPriceResolver.AssetTypeOf(alert.Symbol).ToString(),
            label = MarketPriceResolver.LabelOf(alert.Symbol),
        };
    }

    /// <summary>Current prices for stocks, indices, crypto and USDT/LKR.</summary>
    private Task<Dictionary<string, decimal>> GetCurrentPricesAsync(IEnumerable<string> symbols) =>
        _prices.GetPricesAsync(symbols, HttpContext.RequestAborted);

    private async Task<decimal?> GetCurrentPriceAsync(string symbol)
    {
        var prices = await GetCurrentPricesAsync(new[] { symbol });
        return prices.TryGetValue(symbol, out var price) ? price : null;
    }

    /// <summary>The user's alerts, newest first; <paramref name="symbol"/> narrows to one symbol (the desk's quick-alert popover).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? symbol = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var query = _dbContext.Alerts.AsNoTracking().Where(a => a.CreatedBy == user.Id);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalized = MarketPriceResolver.Normalize(symbol);
            query = query.Where(a => a.Symbol == normalized);
        }

        var alerts = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var prices = await GetCurrentPricesAsync(alerts.Select(a => a.Symbol));

        return Ok(alerts.Select(a =>
            ToDto(a, prices.TryGetValue(a.Symbol, out var price) ? price : null)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(AlertCreateViewModel vm)
    {

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(vm.Symbol))
            return BadRequest(new { message = "Symbol is required." });
        if (vm.TargetPrice <= 0)
            return BadRequest(new { message = "Target price must be greater than 0." });

        // CSE codes are normalised ("COMB" -> "COMB.N0000") so the alert matches stored prices.
        var symbol = MarketPriceResolver.Normalize(vm.Symbol);
        if (!await _prices.IsSupportedAsync(symbol, HttpContext.RequestAborted))
            return BadRequest(new { message = $"Unknown symbol '{vm.Symbol}'." });

        var alert = new Alert
        {
            Symbol = symbol,
            TargetPrice = vm.TargetPrice,
            IsAbove = vm.IsAbove,
            IsActive = vm.IsActive,
            FireOnce = vm.FireOnce,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetById),
            new { id = alert.Id },
            ToDto(alert, await GetCurrentPriceAsync(alert.Symbol))
        );
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, AlertEditViewModel vm)
    {

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = await _dbContext.Alerts
            .FirstOrDefaultAsync(a => a.Id == id && a.CreatedBy == user.Id);

        if (alert == null)
            return NotFound();

        if (vm.TargetPrice <= 0)
            return BadRequest(new { message = "Target price must be greater than 0." });

        alert.TargetPrice = vm.TargetPrice;
        alert.IsAbove = vm.IsAbove;
        alert.IsActive = vm.IsActive;
        if (vm.FireOnce is { } fireOnce) alert.FireOnce = fireOnce;

        await _dbContext.SaveChangesAsync();

        return Ok(ToDto(alert, await GetCurrentPriceAsync(alert.Symbol)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = await _dbContext.Alerts
            .FirstOrDefaultAsync(a => a.Id == id && a.CreatedBy == user.Id);

        if (alert == null)
            return NotFound();

        _dbContext.Alerts.Remove(alert);

        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = await _dbContext.Alerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.CreatedBy == user.Id);

        if (alert == null)
            return NotFound();

        return Ok(ToDto(alert, await GetCurrentPriceAsync(alert.Symbol)));
    }
}
