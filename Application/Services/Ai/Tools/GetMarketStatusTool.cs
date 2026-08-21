using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetMarketStatusTool : IMcpTool
{
    private readonly StockService _stockService;

    public GetMarketStatusTool(StockService stockService)
    {
        _stockService = stockService;
    }

    public string Name => "get_market_status";

    public string Description => "Gets the current overall status of the local stock market (open/closed, indices summary).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    public async Task<string> ExecuteAsync(JsonElement parameters, string userId)
    {
        try
        {
            var data = await _stockService.GetSavedMarketStatusAsync();
            return data != null ? JsonSerializer.Serialize(data) : "Market status not available.";
        }
        catch (Exception ex)
        {
            return $"Error fetching market status: {ex.Message}";
        }
    }
}
