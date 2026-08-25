using System;

namespace dotnetApp.Application.DTOs;

public class RawNewsArticleDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SuggestedCategory { get; set; } = "GLOBAL_BUSINESS"; // CSE_STOCKS, GLOBAL_BUSINESS, GLOBAL_CRYPTO
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}
