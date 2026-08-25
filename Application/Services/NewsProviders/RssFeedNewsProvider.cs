using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Interface;

namespace dotnetApp.Application.Services.NewsProviders;

public class RssFeedNewsProvider : INewsProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderName => "RSS Feeds";

    public RssFeedNewsProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<RawNewsArticleDto>> FetchArticlesAsync(CancellationToken cancellationToken = default)
    {
        var feeds = new (string Url, string Source, string Category)[]
        {
            ("https://www.ft.lk/rss/financial-services", "Daily FT Sri Lanka", "CSE_STOCKS"),
            ("https://feeds.feedburner.com/CoinDesk", "CoinDesk RSS", "GLOBAL_CRYPTO")
        };

        var results = new List<RawNewsArticleDto>();

        foreach (var feed in feeds)
        {
            try
            {
                using var response = await _httpClient.GetAsync(feed.Url, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                var xmlStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var doc = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken);

                foreach (var item in doc.Descendants("item"))
                {
                    var title = item.Element("title")?.Value?.Trim() ?? "";
                    var link = item.Element("link")?.Value?.Trim() ?? "";
                    var description = item.Element("description")?.Value?.Trim() ?? "";
                    var pubDateStr = item.Element("pubDate")?.Value?.Trim();

                    DateTime pubDate = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(pubDateStr) && DateTime.TryParse(pubDateStr, out var parsed))
                    {
                        pubDate = parsed;
                    }

                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
                    {
                        var hashId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(link));
                        results.Add(new RawNewsArticleDto
                        {
                            ExternalId = $"rss_{hashId}",
                            Title = title,
                            Text = description,
                            Url = link,
                            Source = feed.Source,
                            SuggestedCategory = feed.Category,
                            PublishedAt = pubDate
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RssFeedNewsProvider] Error reading feed {feed.Url}: {ex.Message}");
            }
        }

        return results;
    }
}
