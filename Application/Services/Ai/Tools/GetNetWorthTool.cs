using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetNetWorthTool : IMcpTool
{
    private readonly PortfolioService _portfolioService;

    public GetNetWorthTool(PortfolioService portfolioService)
    {
        _portfolioService = portfolioService;
    }

    public string Name => "get_net_worth";

    public string Description => "Gets the total net worth and summary of all portfolios belonging to the current user.";

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
            var netWorth = await _portfolioService.GetNetWorthAsync(userId);
            return JsonSerializer.Serialize(netWorth);
        }
        catch (Exception ex)
        {
            return $"Error fetching net worth: {ex.Message}";
        }
    }
}
