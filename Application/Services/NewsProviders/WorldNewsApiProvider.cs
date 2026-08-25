using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Interface;
using Microsoft.Extensions.Configuration;

namespace dotnetApp.Application.Services.NewsProviders;

public class WorldNewsApiProvider : INewsProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public string ProviderName => "WorldNewsAPI";

    public WorldNewsApiProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<IEnumerable<RawNewsArticleDto>> FetchArticlesAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["WorldNewsApi:ApiKey"];
        var baseUrl = _configuration["WorldNewsApi:BaseUrl"];

        var results = new List<RawNewsArticleDto>();

        // Query 1: Sri Lankan Business News
        var slUrl = $"{baseUrl}/search-news?text=Sri%20Lanka&source-countries=lk&language=en&categories=business&api-key={apiKey}";
        var slArticles = await FetchFromUrlAsync(slUrl, "CSE_STOCKS", cancellationToken);
        results.AddRange(slArticles);

        // Query 2: Global Business & Financial News
        var globalUrl = $"{baseUrl}/search-news?text=finance%20OR%20stock%20market&language=en&categories=business,finance&api-key={apiKey}";
        var globalArticles = await FetchFromUrlAsync(globalUrl, "GLOBAL_BUSINESS", cancellationToken);
        results.AddRange(globalArticles);

        // Query 3: Crypto Market News
        var cryptoUrl = $"{baseUrl}/search-news?text=crypto%20OR%20bitcoin%20OR%20binance&language=en&categories=business&api-key={apiKey}";
        var cryptoArticles = await FetchFromUrlAsync(cryptoUrl, "GLOBAL_CRYPTO", cancellationToken);
        results.AddRange(cryptoArticles);

        return results;
    }

    private async Task<List<RawNewsArticleDto>> FetchFromUrlAsync(string url, string defaultCategory, CancellationToken ct)
    {
        var list = new List<RawNewsArticleDto>();
        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("news", out var newsArray) && newsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in newsArray.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : Guid.NewGuid().ToString();
                    var title = item.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                    var text = item.TryGetProperty("text", out var txProp) ? txProp.GetString() ?? "" : "";
                    var articleUrl = item.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "" : "";
                    var sourceCountry = item.TryGetProperty("source_country", out var cProp) ? cProp.GetString() ?? "" : "";

                    DateTime pubDate = DateTime.UtcNow;
                    if (item.TryGetProperty("publish_date", out var dProp) && DateTime.TryParse(dProp.GetString(), out var parsedDate))
                    {
                        pubDate = parsedDate;
                    }

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        list.Add(new RawNewsArticleDto
                        {
                            ExternalId = $"worldnews_{id}",
                            Title = title,
                            Text = text,
                            Url = articleUrl,
                            Source = string.Equals(sourceCountry, "lk", StringComparison.OrdinalIgnoreCase) ? "Sri Lanka Business News" : "Global WorldNews",
                            SuggestedCategory = defaultCategory,
                            PublishedAt = pubDate
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WorldNewsApiProvider] Error fetching news from {url}: {ex.Message}");
        }

        return list;
    }
}
