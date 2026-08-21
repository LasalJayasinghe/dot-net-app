using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetTopGainersTool : IMcpTool
{
    private readonly StockService _stockService;

    public GetTopGainersTool(StockService stockService)
    {
        _stockService = stockService;
    }

    public string Name => "get_top_gainers";

    public string Description => "Gets a list of the top gaining local stocks for the day.";

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
            var data = await _stockService.GetTopGainers();
            return data != null ? JsonSerializer.Serialize(data) : "Top gainers data not available.";
        }
        catch (Exception ex)
        {
            return $"Error fetching top gainers: {ex.Message}";
        }
    }
}
