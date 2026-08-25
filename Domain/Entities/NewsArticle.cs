using System;

namespace dotnetApp.Domain.Entities;

public class NewsArticle
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty; // Unique hash or API ID for deduplication
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string MarketCategory { get; set; } = string.Empty; // CSE_STOCKS, GLOBAL_BUSINESS, GLOBAL_CRYPTO, MACRO
    public string Sentiment { get; set; } = string.Empty; // BULLISH, BEARISH, NEUTRAL
    public int ImportanceScore { get; set; } // 1 to 10
    public string MentionedTickersJson { get; set; } = "[]"; // JSON array string e.g. ["COMB.N0000", "BTCUSDT"]
    public bool IsModelValidated { get; set; } = false;
    public bool IsRelevant { get; set; } = true;
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
