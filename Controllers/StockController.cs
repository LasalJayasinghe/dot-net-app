using dotnetApp;
using dotnetApp.Models.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Namespace
{
    [Route("api/[controller]")]
    public class StockController : Controller
    {
        private readonly StockService _stockService;

        public StockController(StockService stockService)
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

    }
}
