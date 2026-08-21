using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetStockDataTool : IMcpTool
{
    private readonly StockService _stockService;

    public GetStockDataTool(StockService stockService)
    {
        _stockService = stockService;
    }

    public string Name => "get_stock_price";

    public string Description => "Gets the current price and basic data for a specific local stock symbol (e.g., COMB, SAMP).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            symbol = new
            {
                type = "string",
                description = "The stock symbol to lookup (e.g., COMB, SAMP)"
            }
        },
        required = new[] { "symbol" }
    };

    public async Task<string> ExecuteAsync(JsonElement parameters, string userId)
    {
        try
        {
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("symbol", out var symbolElement))
            {
                var symbol = symbolElement.GetString();
                if (string.IsNullOrEmpty(symbol)) return "Symbol is required.";
                
                var data = await _stockService.GetStockDataAsync(symbol);
                if (data == null) return $"No data found for symbol {symbol}.";
                
                return JsonSerializer.Serialize(data);
            }
            return "Invalid parameters. 'symbol' is required.";
        }
        catch (Exception ex)
        {
            return $"Error fetching stock data: {ex.Message}";
        }
    }
}
