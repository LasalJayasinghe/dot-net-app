using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers
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
            var dataObj = await _stockService.GetStockDataAsync(symbol);

            if (dataObj == null)
                return NotFound(new { message = "Stock not found or API error." });
            var json = JsonSerializer.Serialize(dataObj);

            // 2️⃣ Parse JSON string into JsonNode
            var data = JsonNode.Parse(json);

            // 3️⃣ Extract only the fields you care about
            var result = new
            {
                symbol = data?["reqSymbolInfo"]?["symbol"]?.GetValue<string>(),
                name = data?["reqSymbolInfo"]?["name"]?.GetValue<string>(),
                lastTradedPrice = data?["reqSymbolInfo"]?["lastTradedPrice"]?.GetValue<decimal?>()
            };



            Console.WriteLine($"Extracted Result: {JsonSerializer.Serialize(result)}");
            return Ok(result);
        }
    }
}
