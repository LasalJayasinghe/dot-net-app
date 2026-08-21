using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetTopLosersTool : IMcpTool
{
    private readonly StockService _stockService;

    public GetTopLosersTool(StockService stockService)
    {
        _stockService = stockService;
    }

    public string Name => "get_top_losers";

    public string Description => "Gets a list of the top losing local stocks for the day.";

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
            var data = await _stockService.GetTopLooses();
            return data != null ? JsonSerializer.Serialize(data) : "Top losers data not available.";
        }
        catch (Exception ex)
        {
            return $"Error fetching top losers: {ex.Message}";
        }
    }
}
