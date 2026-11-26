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
    private readonly BinanceService _binance;

    public WatchlistApiController(StockService stockService, AppDbContext dbContext, BinanceService binance)
    {
        _stockService = stockService;
        _dbContext = dbContext;
        _binance = binance;
    }

    /// <summary>
    /// The user's watchlist with current prices. <paramref name="market"/>: "cse" (default, so existing
    /// screens see only stocks), "crypto" or "all".
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? market = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var scope = (market ?? "cse").Trim().ToLowerInvariant();
        var items = await _dbContext.WatchlistItems
            .Where(w => w.UserId == userId)
            .Select(w => new { w.Symbol, w.Market })
            .ToListAsync();

        var results = new List<object>(items.Count);

        if (scope is "crypto" or "all")
        {
            foreach (var c in items.Where(i => i.Market == WatchlistMarkets.Crypto))
            {
                var t = _binance.GetTicker(c.Symbol);
                results.Add(new
                {
                    symbol = c.Symbol,
                    market = WatchlistMarkets.Crypto,
                    price = t?.LastPrice ?? 0,
                    changePct = t?.PriceChangePercent ?? 0,
                    high = t?.High24h ?? 0,
                    low = t?.Low24h ?? 0,
                });
            }
        }

        var symbols = scope is "cse" or "all"
            ? items.Where(i => i.Market == WatchlistMarkets.Cse).Select(i => i.Symbol).ToList()
            : new List<string>();
        if (symbols.Count == 0)
            return Ok(results);

        // Prices come from the Stocks table, which AlertJob refreshes from CSE's tradeSummary every
        // 2 minutes during market hours. Only symbols missing from it fall back to a live CSE call.
        var lookups = symbols.Select(ToLookupSymbol).Distinct().ToList();
        var stored = await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => lookups.Contains(s.Symbol))
            .Select(s => new { s.Symbol, s.Price, s.PercentageChange, s.High, s.Low })
            .ToDictionaryAsync(s => s.Symbol);

        // Sequential on purpose: StockService shares this request's DbContext, which isn't thread-safe.
        var live = new Dictionary<string, dotnetApp.Application.Dtos.StockDataResponseDto?>();
        foreach (var l in lookups.Where(l => !stored.ContainsKey(l)))
            live[l] = await _stockService.GetStockDataAsync(l);

        foreach (var lookup in lookups)
        {
            if (stored.TryGetValue(lookup, out var s))
            {
                results.Add(new
                {
                    symbol = s.Symbol,
                    market = WatchlistMarkets.Cse,
                    price = s.Price,
                    changePct = s.PercentageChange,
                    high = s.High,
                    low = s.Low,
                });
                continue;
            }

            var data = live.GetValueOrDefault(lookup);
            if (data == null) continue;
            results.Add(new
            {
                symbol = data.ReqSymbolInfo.Symbol,
                market = WatchlistMarkets.Cse,
                price = data.ReqSymbolInfo.LastTradedPrice,
                changePct = data.ReqSymbolInfo.PercentageChange,
                high = data.ReqSymbolInfo.High,
                low = data.ReqSymbolInfo.Low,
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

        // Crypto: one of the Binance pairs the app streams (BTCUSDT, ETHUSDT…).
        var isCrypto = string.Equals(dto.Market, WatchlistMarkets.Crypto, StringComparison.OrdinalIgnoreCase)
                       || BinanceService.TrackedSymbols.Contains(symbol);
        if (isCrypto)
        {
            if (!BinanceService.TrackedSymbols.Contains(symbol))
                return BadRequest(new { message = $"{symbol} isn't one of the tracked pairs ({string.Join(", ", BinanceService.TrackedSymbols)})." });
            if (await _dbContext.WatchlistItems.AnyAsync(w => w.UserId == userId && w.Symbol == symbol))
                return Conflict(new { message = "Symbol already in watchlist" });

            _dbContext.WatchlistItems.Add(new WatchlistItem { UserId = userId, Symbol = symbol, Market = WatchlistMarkets.Crypto });
            await _dbContext.SaveChangesAsync();
            var t = _binance.GetTicker(symbol);
            return Ok(new
            {
                symbol,
                market = WatchlistMarkets.Crypto,
                price = t?.LastPrice ?? 0,
                changePct = t?.PriceChangePercent ?? 0,
                high = t?.High24h ?? 0,
                low = t?.Low24h ?? 0,
            });
        }

        var data = await _stockService.GetStockDataAsync(ToLookupSymbol(symbol));
        if (data == null)
            return BadRequest(new { message = "Stock not found" });

        // "COMB" and "COMB.N0000" are the same stock.
        var lookup = ToLookupSymbol(symbol);
        var existing = await _dbContext.WatchlistItems
            .Where(w => w.UserId == userId && w.Market == WatchlistMarkets.Cse)
            .Select(w => w.Symbol)
            .ToListAsync();
        if (existing.Any(e => ToLookupSymbol(e) == lookup))
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
            market = WatchlistMarkets.Cse,
            price = data.ReqSymbolInfo.LastTradedPrice,
            changePct = data.ReqSymbolInfo.PercentageChange,
            high = data.ReqSymbolInfo.High,
            low = data.ReqSymbolInfo.Low,
        });
    }

    [HttpDelete("{symbol}")]
    public async Task<IActionResult> Remove(string symbol)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token - user id missing" });

        var cleanSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        // Exact match first; CSE codes also match their other form ("COMB" ↔ "COMB.N0000").
        var items = await _dbContext.WatchlistItems.Where(w => w.UserId == userId).ToListAsync();
        var item = items.FirstOrDefault(w => w.Symbol == cleanSymbol)
                   ?? items.FirstOrDefault(w => w.Market == WatchlistMarkets.Cse &&
                                                ToLookupSymbol(w.Symbol) == ToLookupSymbol(cleanSymbol));

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

        /// <summary>"Cse" (default) or "Crypto". Tracked Binance pairs are detected without it.</summary>
        public string? Market { get; set; }
    }
}
