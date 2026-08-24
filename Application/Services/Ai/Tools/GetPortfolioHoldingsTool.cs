using System.Text.Json;
using dotnetApp.Application.Services;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetPortfolioHoldingsTool : IMcpTool
{
    private readonly PortfolioService _portfolioService;

    public GetPortfolioHoldingsTool(PortfolioService portfolioService)
    {
        _portfolioService = portfolioService;
    }

    public string Name => "get_portfolio_holdings";

    public string Description => "Gets detailed portfolio holdings for all portfolios (or a specific portfolio) belonging to the current user, including individual assets, symbols, quantities, buy prices, current values, and profit/loss.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            portfolioId = new
            {
                type = "integer",
                description = "Optional portfolio ID to get holdings for a specific portfolio. If omitted, returns holdings for all user portfolios."
            }
        },
        required = Array.Empty<string>()
    };

    public async Task<string> ExecuteAsync(JsonElement parameters, string userId)
    {
        try
        {
            int? portfolioId = null;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("portfolioId", out var idElement))
            {
                if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out int idVal))
                {
                    portfolioId = idVal;
                }
            }

            if (portfolioId.HasValue)
            {
                var detail = await _portfolioService.GetPortfolioDetailAsync(userId, portfolioId.Value);
                if (detail == null) return $"Portfolio with ID {portfolioId.Value} not found.";
                return JsonSerializer.Serialize(detail);
            }
            else
            {
                var portfolios = await _portfolioService.GetUserPortfoliosAsync(userId);
                var details = new List<PortfolioDetailDto>();
                foreach (var p in portfolios)
                {
                    var detail = await _portfolioService.GetPortfolioDetailAsync(userId, p.Id);
                    if (detail != null) details.Add(detail);
                }
                return JsonSerializer.Serialize(details);
            }
        }
        catch (Exception ex)
        {
            return $"Error fetching portfolio holdings: {ex.Message}";
        }
    }
}
