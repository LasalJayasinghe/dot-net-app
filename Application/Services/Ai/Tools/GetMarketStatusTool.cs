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

    public string Description => "Gets the Colombo Stock Exchange status (open/closed, trading day, hours) and the ASPI and S&P SL20 index values.";

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
            var status = await _stockService.GetSavedMarketStatusAsync();
            var aspi = await _stockService.GetSavedAspiDataAsync();
            var snp = await _stockService.GetSavedSnpDataAsync();

            return JsonSerializer.Serialize(new
            {
                market = status == null ? null : new
                {
                    status.IsOpen,
                    status.IsTradingDay,
                    OpenTime = status.OpenTime.ToString("HH:mm"),
                    CloseTime = status.CloseTime.ToString("HH:mm"),
                    TimeZone = "Asia/Colombo",
                    status.UpdatedAt,
                },
                indices = new[]
                {
                    new { Name = "ASPI", Value = aspi.value, Change = aspi.change, ChangePercent = aspi.percentage, High = aspi.highValue, Low = aspi.lowValue },
                    new { Name = "S&P SL20", Value = snp.value, Change = snp.change, ChangePercent = snp.percentage, High = snp.highValue, Low = snp.lowValue },
                },
            });
        }
        catch (Exception ex)
        {
            return $"Error fetching market status: {ex.Message}";
        }
    }
}
