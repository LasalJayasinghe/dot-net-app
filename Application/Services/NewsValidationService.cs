using System;
using System.Collections.Concurrent;
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
    /// <summary>Returned when Ollama is unreachable, so the job backs off instead of hammering it.</summary>
    public const int OllamaUnavailable = -1;

    private const int MaxAttemptsPerArticle = 3;
    private const int SnippetLength = 600;

    // Per-article failure counts (static: the service is scoped, the job creates a new one per batch).
    private static readonly ConcurrentDictionary<int, int> FailedAttempts = new();
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
        sb.AppendLine("- reasoning: (1-2 sentence explanation of why the AI assigned this relevance, sentiment, and category)");
        sb.AppendLine("\nArticles:");

        for (int idx = 0; idx < pendingArticles.Count; idx++)
        {
            var text = !string.IsNullOrWhiteSpace(pendingArticles[idx].Summary) ? pendingArticles[idx].Summary : pendingArticles[idx].Title;
            var snippet = text.Length > SnippetLength ? text.Substring(0, SnippetLength) + "..." : text;
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
                num_predict = 1500, // ~250 tokens per article; 400 truncated the JSON for 5-article batches
                temperature = 0.1
            }
        };

        string llmResponse;
        try
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{baseUrl}/api/generate", jsonContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama returned HTTP {StatusCode} during news validation; will retry later.", response.StatusCode);
                return OllamaUnavailable;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            llmResponse = doc.RootElement.TryGetProperty("response", out var respProp) ? respProp.GetString() ?? "" : "";
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // Ollama down/unreachable: leave the articles pending (don't mark them validated with defaults).
            _logger.LogWarning("Ollama unreachable for news validation ({Error}); will retry later.", ex.Message);
            return OllamaUnavailable;
        }

        var knownTickers = await LoadKnownTickersAsync(ct);
        var processed = ParseAndUpdateBatch(llmResponse, pendingArticles, knownTickers);

        // Articles the model skipped or answered with unparseable output: retry a few times, then give up
        // explicitly so one bad article can't block the queue forever.
        foreach (var article in pendingArticles.Where(a => !processed.Contains(a.Id)))
        {
            var attempts = FailedAttempts.AddOrUpdate(article.Id, 1, (_, n) => n + 1);
            if (attempts >= MaxAttemptsPerArticle)
            {
                article.IsModelValidated = true;
                article.ValidationReasoning = $"AI validation failed after {attempts} attempts; using ingestion defaults.";
                FailedAttempts.TryRemove(article.Id, out _);
                _logger.LogWarning("News article {Id} could not be validated after {Attempts} attempts.", article.Id, attempts);
            }
        }
        foreach (var id in processed) FailedAttempts.TryRemove(id, out _);

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("News validation: {Ok}/{Total} articles validated.", processed.Count, pendingArticles.Count);
        return pendingArticles.Count;
    }

    /// <summary>CSE symbols from the Stocks table plus the crypto pairs the app tracks.</summary>
    private async Task<HashSet<string>> LoadKnownTickersAsync(CancellationToken ct)
    {
        var cse = await _dbContext.Stocks.AsNoTracking().Select(s => s.Symbol).ToListAsync(ct);
        var set = new HashSet<string>(cse, StringComparer.OrdinalIgnoreCase);
        set.UnionWith(BinanceService.TrackedSymbols);
        return set;
    }

    /// <summary>
    /// Maps a model-suggested ticker to a real one: "COMB" -> "COMB.N0000", "BTC" -> "BTCUSDT".
    /// Returns null for anything that isn't a listed CSE security or a tracked crypto pair.
    /// </summary>
    private static string? NormalizeTicker(string raw, HashSet<string> known)
    {
        var t = raw.Trim().ToUpperInvariant();
        if (t.Length == 0) return null;
        foreach (var candidate in new[] { t, CseSymbols.ToLookup(t), t.Replace("/", ""), t + "USDT" })
            if (known.TryGetValue(candidate, out var match)) return match;
        return null;
    }

    private HashSet<int> ParseAndUpdateBatch(string llmResponse, List<NewsArticle> batch, HashSet<string> knownTickers)
    {
        var processed = new HashSet<int>();
        try
        {
            using var doc = JsonDocument.Parse(llmResponse);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("articles", out var arrProp))
                root = arrProp;

            // Single-article batches sometimes come back as a bare object.
            var elements = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object when root.TryGetProperty("index", out _) => new List<JsonElement> { root },
                _ => new List<JsonElement>(),
            };

            foreach (var element in elements)
            {
                if (!element.TryGetProperty("index", out var idxProp) || !idxProp.TryGetInt32(out var index) || index < 0 || index >= batch.Count)
                    continue;

                var article = batch[index];

                var isRelevant = !element.TryGetProperty("is_relevant", out var relProp) || relProp.ValueKind != JsonValueKind.False;
                var cat = element.TryGetProperty("category", out var cProp) ? cProp.GetString() ?? article.MarketCategory : article.MarketCategory;
                var sent = element.TryGetProperty("sentiment", out var sProp) ? sProp.GetString() ?? "NEUTRAL" : "NEUTRAL";
                var imp = element.TryGetProperty("importance", out var imProp) && imProp.TryGetInt32(out var impVal) ? Math.Clamp(impVal, 1, 10) : 5;
                var summary = element.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? article.Summary : article.Summary;
                var reasoning = element.TryGetProperty("reasoning", out var rProp) ? rProp.GetString() ?? "" : "";

                // Keep only tickers that are real CSE securities or tracked crypto pairs.
                var tickers = new List<string>();
                if (element.TryGetProperty("tickers", out var tickArray) && tickArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tickArray.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String && NormalizeTicker(t.GetString()!, knownTickers) is { } valid && !tickers.Contains(valid))
                            tickers.Add(valid);
                    }
                }

                article.IsRelevant = isRelevant;
                article.MarketCategory = string.IsNullOrWhiteSpace(cat) ? article.MarketCategory : cat.ToUpperInvariant();
                article.Sentiment = sent.ToUpperInvariant();
                article.ImportanceScore = imp;
                if (!string.IsNullOrWhiteSpace(summary)) article.Summary = summary;
                article.ValidationReasoning = !string.IsNullOrWhiteSpace(reasoning)
                    ? reasoning
                    : $"Classified as {sent} for {cat} based on article headline and summary signals.";
                article.MentionedTickersJson = JsonSerializer.Serialize(tickers);
                article.IsModelValidated = true;
                processed.Add(article.Id);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Ollama validation response JSON.");
        }

        return processed;
    }
}
