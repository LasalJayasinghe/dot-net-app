using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Interface;
using dotnetApp.Domain.Entities;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Application.Services;

public class NewsIngestionService
{
    private readonly IEnumerable<INewsProvider> _newsProviders;
    private readonly AppDbContext _dbContext;

    private static readonly string[] CseKeywords = new[]
    {
        "cse", "colombo stock exchange", "central bank", "cbsl", "lkr", "treasury", "sec",
        "commercial bank", "comb", "sampath", "samp", "john keells", "jkh", "hayleys",
        "inflation", "imf", "gdp", "ipo", "dividend", "sri lanka", "rupiah", "central bank of sri lanka"
    };

    private static readonly string[] GlobalKeywords = new[]
    {
        "bitcoin", "btc", "ethereum", "eth", "binance", "crypto", "fed", "federal reserve",
        "stock market", "wall street", "s&p", "nasdaq", "interest rates", "inflation", "tariff"
    };

    public NewsIngestionService(
        IEnumerable<INewsProvider> newsProviders,
        AppDbContext dbContext)
    {
        _newsProviders = newsProviders;
        _dbContext = dbContext;
    }

    public async Task<int> IngestLatestNewsAsync(CancellationToken ct = default)
    {
        Console.WriteLine("[NewsIngestionService] Starting news ingestion from all providers...");

        // 1. Fetch raw articles from all providers
        var rawArticles = new List<RawNewsArticleDto>();
        foreach (var provider in _newsProviders)
        {
            try
            {
                var articles = await provider.FetchArticlesAsync(ct);
                rawArticles.AddRange(articles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NewsIngestionService] Error running provider {provider.ProviderName}: {ex.Message}");
            }
        }

        if (!rawArticles.Any())
        {
            Console.WriteLine("[NewsIngestionService] No raw articles fetched.");
            return 0;
        }

        // Deduplicate in-memory by ExternalId
        var distinctArticles = rawArticles
            .GroupBy(a => a.ExternalId)
            .Select(g => g.First())
            .ToList();

        // 2. Exclude articles that already exist in DB
        var externalIds = distinctArticles.Select(a => a.ExternalId).ToList();
        var existingIds = await _dbContext.NewsArticles
            .Where(n => externalIds.Contains(n.ExternalId))
            .Select(n => n.ExternalId)
            .ToListAsync(ct);

        var newArticles = distinctArticles.Where(a => !existingIds.Contains(a.ExternalId)).ToList();
        if (!newArticles.Any())
        {
            Console.WriteLine("[NewsIngestionService] All fetched articles already exist in DB.");
            return 0;
        }

        // 3. Tier 1: Fast Rule & Keyword Filtering
        var candidateArticles = newArticles.Where(IsTier1Candidate).ToList();
        Console.WriteLine($"[NewsIngestionService] Filtered {newArticles.Count} new articles down to {candidateArticles.Count} candidates via Tier 1 filter.");

        if (!candidateArticles.Any()) return 0;

        // 4. Convert candidate articles to NewsArticle entities (IsModelValidated = false)
        var entitiesToSave = candidateArticles.Select(item => new NewsArticle
        {
            ExternalId = item.ExternalId,
            Title = item.Title,
            Summary = item.Text.Length > 250 ? item.Text.Substring(0, 250) + "..." : item.Text,
            OriginalUrl = item.Url,
            Source = item.Source,
            MarketCategory = item.SuggestedCategory,
            Sentiment = "NEUTRAL",
            ImportanceScore = 5,
            MentionedTickersJson = "[]",
            IsModelValidated = false, // Will be validated asynchronously by NewsValidationJob
            IsRelevant = true,
            PublishedAt = item.PublishedAt
        }).ToList();

        // 5. Save to DB immediately (Unvalidated articles queued for NewsValidationJob)
        await _dbContext.NewsArticles.AddRangeAsync(entitiesToSave, ct);
        await _dbContext.SaveChangesAsync(ct);

        Console.WriteLine($"[NewsIngestionService] Successfully ingested and saved {entitiesToSave.Count} new articles to DB (pending AI validation).");
        return entitiesToSave.Count;
    }

    private bool IsTier1Candidate(RawNewsArticleDto article)
    {
        var textToScan = $"{article.Title} {article.Text}".ToLowerInvariant();
        return CseKeywords.Any(kw => textToScan.Contains(kw)) || GlobalKeywords.Any(kw => textToScan.Contains(kw));
    }
}
