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

    public AlertApiController(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
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
            createdAt = alert.CreatedAt
        };
    }

    private async Task<Dictionary<string, decimal>> GetCurrentPricesAsync(IEnumerable<string> symbols)
    {
        var distinct = symbols.Distinct().ToList();
        if (distinct.Count == 0) return new Dictionary<string, decimal>();

        return await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => distinct.Contains(s.Symbol))
            .ToDictionaryAsync(s => s.Symbol, s => s.Price);
    }

    private async Task<decimal?> GetCurrentPriceAsync(string symbol)
    {
        var prices = await GetCurrentPricesAsync(new[] { symbol });
        return prices.TryGetValue(symbol, out var price) ? price : null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alerts = await _dbContext.Alerts
            .AsNoTracking()
            .Where(a => a.CreatedBy == user.Id)
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

        var alert = new Alert
        {
            Symbol = vm.Symbol,
            TargetPrice = vm.TargetPrice,
            IsAbove = vm.IsAbove,
            IsActive = vm.IsActive,
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

        alert.TargetPrice = vm.TargetPrice;
        alert.IsAbove = vm.IsAbove;
        alert.IsActive = vm.IsActive;

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
