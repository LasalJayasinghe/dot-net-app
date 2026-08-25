using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Domain.Entities;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services;

public class NewsValidationService
{
    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NewsValidationService> _logger;

    public NewsValidationService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<NewsValidationService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Processes pending unvalidated news articles using Ollama LLM.
    /// </summary>
    /// <returns>Number of articles processed in this run.</returns>
    public async Task<int> ValidatePendingArticlesAsync(CancellationToken ct = default)
    {
        // 1. Query pending unvalidated articles
        var pendingArticles = await _dbContext.NewsArticles
            .Where(n => !n.IsModelValidated)
            .OrderBy(n => n.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        if (!pendingArticles.Any())
        {
            return 0;
        }

        _logger.LogInformation("Found {Count} pending unvalidated news articles. Starting Ollama validation...", pendingArticles.Count);

        var baseUrl = _configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        var modelName = _configuration["Ollama:Model"] ?? "qwen3.5:4b";

        var sb = new StringBuilder();
        sb.AppendLine("Analyze the following financial news articles. Return a JSON array where each object has:");
        sb.AppendLine("- index: (number matching the item index)");
        sb.AppendLine("- is_relevant: (boolean, true if related to Sri Lankan stock market, crypto, or global finance)");
        sb.AppendLine("- category: (\"CSE_STOCKS\", \"GLOBAL_BUSINESS\", \"GLOBAL_CRYPTO\", or \"MACRO\")");
        sb.AppendLine("- sentiment: (\"BULLISH\", \"BEARISH\", or \"NEUTRAL\")");
        sb.AppendLine("- importance: (number 1-10)");
        sb.AppendLine("- tickers: (array of stock/crypto symbols, e.g. [\"COMB.N0000\", \"BTCUSDT\"])");
        sb.AppendLine("- summary: (1-2 sentence trader summary)");
        sb.AppendLine("\nArticles:");

        for (int idx = 0; idx < pendingArticles.Count; idx++)
        {
            var text = !string.IsNullOrWhiteSpace(pendingArticles[idx].Summary) ? pendingArticles[idx].Summary : pendingArticles[idx].Title;
            var snippet = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
            sb.AppendLine($"[Item {idx}] Title: {pendingArticles[idx].Title} | Content: {snippet}");
        }

        var requestBody = new
        {
            model = modelName,
            prompt = sb.ToString(),
            stream = false,
            format = "json",
            options = new
            {
                num_predict = 300,
                temperature = 0.1
            }
        };

        try
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{baseUrl}/api/generate", jsonContent, ct);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(responseJson);

                if (doc.RootElement.TryGetProperty("response", out var respProp))
                {
                    var llmResponse = respProp.GetString() ?? "";
                    ParseAndUpdateBatch(llmResponse, pendingArticles);
                }
            }
            else
            {
                _logger.LogWarning("Ollama returned HTTP status {StatusCode} during validation", response.StatusCode);
                MarkBatchAsValidatedWithFallback(pendingArticles);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating news batch with Ollama LLM");
            MarkBatchAsValidatedWithFallback(pendingArticles);
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Completed validation for {Count} articles.", pendingArticles.Count);
        return pendingArticles.Count;
    }

    private void ParseAndUpdateBatch(string llmResponse, List<NewsArticle> batch)
    {
        try
        {
            using var doc = JsonDocument.Parse(llmResponse);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("articles", out var arrProp))
            {
                root = arrProp;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                var processedIndices = new HashSet<int>();

                foreach (var element in root.EnumerateArray())
                {
                    if (element.TryGetProperty("index", out var idxProp) && idxProp.TryGetInt32(out var index) && index >= 0 && index < batch.Count)
                    {
                        var article = batch[index];
                        processedIndices.Add(index);

                        var isRelevant = !element.TryGetProperty("is_relevant", out var relProp) || relProp.GetBoolean();
                        var cat = element.TryGetProperty("category", out var cProp) ? cProp.GetString() ?? article.MarketCategory : article.MarketCategory;
                        var sent = element.TryGetProperty("sentiment", out var sProp) ? sProp.GetString() ?? "NEUTRAL" : "NEUTRAL";
                        var imp = element.TryGetProperty("importance", out var imProp) && imProp.TryGetInt32(out var impVal) ? impVal : 5;
                        var summary = element.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? article.Summary : article.Summary;

                        var tickers = new List<string>();
                        if (element.TryGetProperty("tickers", out var tickArray) && tickArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var t in tickArray.EnumerateArray())
                            {
                                if (t.ValueKind == JsonValueKind.String) tickers.Add(t.GetString()!);
                            }
                        }

                        article.IsRelevant = isRelevant;
                        article.MarketCategory = string.IsNullOrWhiteSpace(cat) ? article.MarketCategory : cat.ToUpperInvariant();
                        article.Sentiment = sent.ToUpperInvariant();
                        article.ImportanceScore = imp;
                        if (!string.IsNullOrWhiteSpace(summary)) article.Summary = summary;
                        if (tickers.Any()) article.MentionedTickersJson = JsonSerializer.Serialize(tickers);
                        article.IsModelValidated = true;
                    }
                }

                // Any unprocessed items in batch get marked as validated with default values
                for (int i = 0; i < batch.Count; i++)
                {
                    if (!processedIndices.Contains(i))
                    {
                        batch[i].IsModelValidated = true;
                    }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Ollama validation response JSON.");
        }

        MarkBatchAsValidatedWithFallback(batch);
    }

    private static void MarkBatchAsValidatedWithFallback(List<NewsArticle> batch)
    {
        foreach (var article in batch)
        {
            article.IsModelValidated = true;
        }
    }
}
