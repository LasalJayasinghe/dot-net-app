using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dotnetApp.Infrastructure.Data;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/watchlist")]
[Authorize]
public class WatchlistApiController : ControllerBase
{
    private readonly StockService _stockService;
    private readonly AppDbContext _dbContext;

    public WatchlistApiController(StockService stockService, AppDbContext dbContext)
    {
        _stockService = stockService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var symbols = await _dbContext.WatchlistItems
            .Where(w => w.UserId == userId)
            .Select(w => w.Symbol)
            .ToListAsync();

        if (symbols.Count == 0)
            return Ok(Array.Empty<object>());

        var results = new List<object>();
        foreach (var symbol in symbols)
        {
            var data = await _stockService.GetStockDataAsync(ToLookupSymbol(symbol));
            if (data == null) continue;

            results.Add(new
            {
                symbol = data.ReqSymbolInfo.Symbol,
                price = data.ReqSymbolInfo.LastTradedPrice,
                changePct = data.ReqSymbolInfo.PercentageChange,
            });
        }

        return Ok(results);
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddWatchlistDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var symbol = (dto.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { message = "Symbol is required" });

        var data = await _stockService.GetStockDataAsync(ToLookupSymbol(symbol));
        if (data == null)
            return BadRequest(new { message = "Stock not found" });

        var exists = await _dbContext.WatchlistItems
            .AnyAsync(w => w.UserId == userId && w.Symbol == symbol);
        if (exists)
            return Conflict(new { message = "Symbol already in watchlist" });

        _dbContext.WatchlistItems.Add(new WatchlistItem
        {
            UserId = userId,
            Symbol = symbol,
        });
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            symbol = data.ReqSymbolInfo.Symbol,
            price = data.ReqSymbolInfo.LastTradedPrice,
            changePct = data.ReqSymbolInfo.PercentageChange,
        });
    }

    [HttpDelete("{symbol}")]
    public async Task<IActionResult> Remove(string symbol)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var cleanSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        var item = await _dbContext.WatchlistItems
            .FirstOrDefaultAsync(w => w.UserId == userId && w.Symbol == cleanSymbol);

        if (item == null)
            return NotFound(new { message = "Symbol not found in watchlist" });

        _dbContext.WatchlistItems.Remove(item);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    private static string ToLookupSymbol(string input)
    {
        return input.Contains('.') ? input : $"{input}.N0000";
    }

    public class AddWatchlistDto
    {
        public string? Symbol { get; set; }
    }
}
