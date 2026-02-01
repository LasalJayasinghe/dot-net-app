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
    public async Task<IActionResult> Post([FromBody] JsonElement value)
    {
        Console.WriteLine("Received Telegram webhook");
        if (!value.TryGetProperty("message", out var message))
            return Ok();

        if (!message.TryGetProperty("text", out var textProp))
            return Ok();

        var text = textProp.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return Ok();

        long chatId = message.GetProperty("chat").GetProperty("id").GetInt64();

        text = text.Trim().ToUpper();
        var parts = text.Split('-', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            await _telegramService.SendMessageAsync(
                chatId,
                "Invalid format. Use: SYMBOL-LIMIT (example: AAPL-150)"
            );
            return Ok();
        }

        var symbol = parts[0];
        var limitValue = parts[1];

        Console.WriteLine($"Fetching stock data for symbol: {symbol}");

        var dataObj = await _stockService.GetStockDataAsync(symbol +".N0000");

        if (dataObj == null)
        {
            await _telegramService.SendMessageAsync(chatId, "Stock not found.");
            return Ok();
        }

        var json = JsonSerializer.Serialize(dataObj);
        var data = JsonNode.Parse(json);

        var result = new
        {
            symbol = data?["reqSymbolInfo"]?["symbol"]?.GetValue<string>(),
            lastTradedPrice = data?["reqSymbolInfo"]?["lastTradedPrice"]?.GetValue<decimal?>()
        };

        await _telegramService.SendMessageAsync(
            chatId,
            $"{result.symbol} last traded price: {result.lastTradedPrice}"
        );

        return Ok();
    }

}