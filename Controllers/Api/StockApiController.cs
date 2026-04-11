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

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetStock(string symbol)
    {
        var data = await _stockService.GetStockDataAsync(symbol);
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
}
