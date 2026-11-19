using dotnetApp.Application.Dtos;
using dotnetApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/stocks")]
[Authorize]
public class StockApiController : ControllerBase
{
    private readonly StockService _stockService;
    private readonly CseHistoryService _history;

    public StockApiController(StockService stockService, CseHistoryService history)
    {
        _stockService = stockService;
        _history = history;
    }

    [HttpGet("details/{symbol}")]
    public async Task<IActionResult> GetStock(string symbol)
    {
        var data = await _stockService.GetStockDataAsync(CseSymbols.ToLookup(symbol));
        if (data == null)
            return NotFound(new { message = "Stock not found or API error." });

        var result = new
        {
            Symbol = data.ReqSymbolInfo.Symbol,
            Price = data.ReqSymbolInfo.ClosingPrice,
            LastTradedPrice = data.ReqSymbolInfo.LastTradedPrice
        };

        return Ok(result);
    }

    /// <summary>
    /// Real CSE price history. range = 1D | 1W | 1M | 3M | 1Y | ALL.
    /// Daily ranges come from our stored history (CsePriceDaily, which grows past CSE's 1-year limit)
    /// once it covers the range; otherwise, and for 1D, from the live CSE chart.
    /// </summary>
    [HttpGet("{symbol}/history")]
    public async Task<IActionResult> GetHistory(string symbol, [FromQuery] string range = "1M", CancellationToken ct = default)
    {
        var stored = (range ?? "").ToUpperInvariant() == "1D" ? null : await _history.GetDailyHistoryAsync(symbol, range ?? "1M", ct);
        var points = stored ?? await _stockService.GetPriceHistoryAsync(symbol, range ?? "1M");
        if (points == null)
            return NotFound(new { message = "No price history available for this symbol." });

        return Ok(points.Select(p => new
        {
            time = p.Time,
            open = p.Open,
            high = p.High,
            low = p.Low,
            close = p.Close,
            volume = p.Volume,
        }));
    }

    /// <summary>
    /// Today's 10 most active CSE stocks by share volume and by turnover (from the latest tradeSummary).
    /// </summary>
    [HttpGet("most-active")]
    public async Task<IActionResult> GetMostActive([FromQuery] int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 50);
        var activity = await _stockService.GetActivitySnapshotAsync();
        var traded = activity.Values.Where(a => a.ShareVolume > 0).ToList();

        object Row(StockActivityDto a) => new
        {
            symbol = a.Symbol,
            name = a.Name,
            price = a.Price,
            changePercentage = a.PercentageChange,
            shareVolume = a.ShareVolume,
            tradeVolume = a.TradeVolume,
            turnover = a.Turnover,
        };

        return Ok(new
        {
            byVolume = traded.OrderByDescending(a => a.ShareVolume).Take(limit).Select(Row),
            byTurnover = traded.OrderByDescending(a => a.Turnover).Take(limit).Select(Row),
        });
    }

    [HttpGet("intraday")]
    public async Task<IActionResult> GetIntradayData()
    {
        var data = await _stockService.GetIntradayDataAsync();
        if (data == null)
            return NotFound(new { message = "Intraday data not found or API error." });

        return Ok(data);
    }

    [HttpGet("names")]
    public async Task<IActionResult> GetStockNames()
    {
        var stocks = await _stockService.GetAllStockNamesAsync();

        var result = stocks
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                symbol = s.Symbol,
                name = s.Name
            });

        return Ok(result);
    }

    [HttpGet("market-status")]
    public async Task<IActionResult> GetMarketStatus()
    {
        var status = await _stockService.GetSavedMarketStatusAsync();
        if (status == null)
            return NotFound(new { message = "Market status not found." });

        return Ok(new
        {
            isTradingDay = status.IsTradingDay,
            isOpen = status.IsOpen,
            openTime = status.OpenTime,
            closeTime = status.CloseTime,
            updatedAt = status.UpdatedAt
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var stocks = await _stockService.GetStockTickersAsync();
        var result = stocks.Select(s => new
        {
            symbol = s.Symbol,
            name = s.Name,
            price = s.Price,
            previousClose = s.PreviousClose,
            high = s.High,
            low = s.Low,
            percentageChange = s.PercentageChange,
            change = s.Change
        });
        return Ok(result);
    }

    [HttpGet("movers")]
    public async Task<IActionResult> GetMovers()
    {
        var gainers = await _stockService.GetTopGainers() ?? new List<StockTopDto>();
        var losers = await _stockService.GetTopLooses() ?? new List<StockTopDto>();
        return Ok(new { gainers, losers });
    }

    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices()
    {
        var aspi = await _stockService.GetSavedAspiDataAsync();
        var snp = await _stockService.GetSavedSnpDataAsync();
        return Ok(new { aspi, snp });
    }
}
