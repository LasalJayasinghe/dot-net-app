using dotnetApp.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/stocks")]
[Authorize]
public class StockApiController : ControllerBase
{
    private readonly StockService _stockService;

    public StockApiController(StockService stockService)
    {
        _stockService = stockService;
    }

    [HttpGet("details/{symbol}")]
    public async Task<IActionResult> GetStock(string symbol)
    {
        var lookup = symbol.Contains('.') ? symbol : $"{symbol}.N0000";
        var data = await _stockService.GetStockDataAsync(lookup);
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
        var gainers = await _stockService.GetTopGainers();
        var losers = await _stockService.GetTopLooses();
        return Ok(new { gainers, losers });
    }

    [HttpGet("indices")]
    public async Task<IActionResult> GetIndices()
    {
        var aspi = await _stockService.GetASPIData();
        var snp = await _stockService.GetSnpData();
        return Ok(new { aspi, snp });
    }
}
