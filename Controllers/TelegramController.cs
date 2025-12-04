using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers;

[Route("api/[controller]")]
public class TelegramController : Controller
{
    private readonly TelegramService _telegramService;
    private readonly StockService _stockService;


    public TelegramController(TelegramService telegramService, StockService stockService)
    {
        _telegramService = telegramService;
        _stockService = stockService;
    }

    [HttpPost]
    public IActionResult Post([FromBody] JsonElement value)
    {

        if (value.TryGetProperty("message", out var message))
        {
            string? text = message.GetProperty("text").GetString();
            long chatId = message.GetProperty("chat").GetProperty("id").GetInt64();

            Console.WriteLine("Message text:" + text);

            if (text != null)
            {
                text = text.Trim().ToUpper();
                var limitValue = text.Split('-')[1];
                var symbol = text.Split('-')[0];

                Console.WriteLine($"Symbol: {symbol}, Limit: {limitValue}");
                //Cache ymbol and limitValue  fpr future use , thse casche values will be used for a
                var dataObj = _stockService.GetStockDataAsync(symbol ?? "");

                if (dataObj == null)
                    return NotFound(new { message = "Stock not found or API error." });
                var json = JsonSerializer.Serialize(dataObj);
                var data = JsonNode.Parse(json);

                var result = new
                {
                    symbol = data?["reqSymbolInfo"]?["symbol"]?.GetValue<string>(),
                    name = data?["reqSymbolInfo"]?["name"]?.GetValue<string>(),
                    lastTradedPrice = data?["reqSymbolInfo"]?["lastTradedPrice"]?.GetValue<decimal?>()
                };

                _telegramService.SendMessageAsync($" {result.symbol} last traded price: {result.lastTradedPrice} : {DateTime.Now}").Wait();

                return Ok(new { receivedText = text, chatId });

            }
            else
            {
                Console.WriteLine("No valid text found in the message.");
                return Ok(new { message = "No valid text found in the message." });
            }
        }

        return Ok(new { message = "Payload received successfully" });
    }
}