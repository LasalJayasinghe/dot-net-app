using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Application.Services.Ai.Tools;

public class GetMarketNewsTool : IMcpTool
{
    private readonly AppDbContext _dbContext;

    public GetMarketNewsTool(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "get_market_news";

    public string Description => "Gets the latest validated financial news, sentiment analysis, and ticker mentions for Sri Lankan CSE stock market, crypto, or global business.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            category = new
            {
                type = "string",
                description = "Optional filter: 'CSE_STOCKS', 'GLOBAL_BUSINESS', 'GLOBAL_CRYPTO', or 'MACRO'"
            },
            symbol = new
            {
                type = "string",
                description = "Optional ticker symbol (e.g. COMB, SAMP, BTCUSDT)"
            },
            limit = new
            {
                type = "integer",
                description = "Number of articles to fetch (default: 5)"
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement parameters, string userId)
    {
        try
        {
            string? category = null;
            string? symbol = null;
            int limit = 5;

            if (parameters.ValueKind == JsonValueKind.Object)
            {
                if (parameters.TryGetProperty("category", out var catEl))
                {
                    category = catEl.GetString();
                }
                if (parameters.TryGetProperty("symbol", out var symEl))
                {
                    symbol = symEl.GetString();
                }
                if (parameters.TryGetProperty("limit", out var limEl) && limEl.TryGetInt32(out var limVal))
                {
                    limit = limVal > 20 ? 20 : limVal;
                }
            }

            var query = _dbContext.NewsArticles.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(n => n.MarketCategory.ToLower() == category.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var cleanSym = symbol.Trim().ToUpperInvariant();
                query = query.Where(n => n.MentionedTickersJson.Contains(cleanSym) || n.Title.Contains(cleanSym));
            }

            var articles = await query
                .OrderByDescending(n => n.PublishedAt)
                .Take(limit)
                .Select(n => new
                {
                    n.Title,
                    n.Summary,
                    n.Source,
                    Category = n.MarketCategory,
                    n.Sentiment,
                    n.ImportanceScore,
                    Tickers = n.MentionedTickersJson,
                    n.PublishedAt
                })
                .ToListAsync();

            if (!articles.Any())
            {
                return "No recent news found matching the specified filters.";
            }

            return JsonSerializer.Serialize(articles);
        }
        catch (Exception ex)
        {
            return $"Error fetching market news: {ex.Message}";
        }
    }
}
