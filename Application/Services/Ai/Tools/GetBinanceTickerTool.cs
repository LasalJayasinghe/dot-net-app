using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetBinanceTickerTool : IMcpTool
{
    private readonly BinanceService _binanceService;

    public GetBinanceTickerTool(BinanceService binanceService)
    {
        _binanceService = binanceService;
    }

    public string Name => "get_binance_ticker";

    public string Description => "Gets the 24-hour ticker data for a specific cryptocurrency on Binance (e.g., BTCUSDT, ETHUSDT).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            symbol = new
            {
                type = "string",
                description = "The crypto symbol pair to lookup (e.g., BTCUSDT)"
            }
        },
        required = new[] { "symbol" }
    };

    public Task<string> ExecuteAsync(JsonElement parameters, string userId)
    {
        try
        {
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("symbol", out var symbolElement))
            {
                var symbol = symbolElement.GetString();
                if (string.IsNullOrEmpty(symbol)) return Task.FromResult("Symbol is required.");
                
                var data = _binanceService.GetTicker(symbol);
                if (data == null) return Task.FromResult($"No active stream data found for symbol {symbol}. Tracked symbols are limited.");
                
                return Task.FromResult(JsonSerializer.Serialize(data));
            }
            return Task.FromResult("Invalid parameters. 'symbol' is required.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error fetching binance data: {ex.Message}");
        }
    }
}
