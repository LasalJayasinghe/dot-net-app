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

        // Prices come from the Stocks table, which AlertJob refreshes from CSE's tradeSummary every
        // 2 minutes during market hours. Only symbols missing from it fall back to a live CSE call.
        var lookups = symbols.Select(ToLookupSymbol).Distinct().ToList();
        var stored = await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => lookups.Contains(s.Symbol))
            .Select(s => new { s.Symbol, s.Price, s.PercentageChange })
            .ToDictionaryAsync(s => s.Symbol);

        // Sequential on purpose: StockService shares this request's DbContext, which isn't thread-safe.
        var live = new Dictionary<string, dotnetApp.Application.Dtos.StockDataResponseDto?>();
        foreach (var l in lookups.Where(l => !stored.ContainsKey(l)))
            live[l] = await _stockService.GetStockDataAsync(l);

        var results = new List<object>(symbols.Count);
        foreach (var lookup in lookups)
        {
            if (stored.TryGetValue(lookup, out var s))
            {
                results.Add(new { symbol = s.Symbol, price = s.Price, changePct = s.PercentageChange });
                continue;
            }

            var data = live.GetValueOrDefault(lookup);
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

    private static string ToLookupSymbol(string input) => CseSymbols.ToLookup(input);

    public class AddWatchlistDto
    {
        public string? Symbol { get; set; }
    }
}
