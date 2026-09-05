using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dotnetApp.Application.Services;
using dotnetApp.Domain.Entities;
using dotnetApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/news")]
public class NewsApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly NewsIngestionService _newsIngestionService;
    private readonly ILogger<NewsApiController> _logger;

    public NewsApiController(
        AppDbContext dbContext,
        NewsIngestionService newsIngestionService,
        ILogger<NewsApiController> logger)
    {
        _dbContext = dbContext;
        _newsIngestionService = newsIngestionService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetNews(
        [FromQuery] string? category,
        [FromQuery] string? sentiment,
        [FromQuery] int limit = 20,
        [FromQuery] int page = 1)
    {
        if (limit > 100) limit = 100;
        if (page < 1) page = 1;

        var query = _dbContext.NewsArticles.AsNoTracking().Where(n => n.IsRelevant).AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(n => n.MarketCategory.ToLower() == category.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(sentiment))
        {
            query = query.Where(n => n.Sentiment.ToLower() == sentiment.ToLower());
        }

        var totalItems = await query.CountAsync();
        var articles = await query
            .OrderByDescending(n => n.PublishedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var paymentDatesByExternalId = await _dbContext.Dividends
            .AsNoTracking()
            .ToDictionaryAsync(d => $"cse-dividend-{d.Id}", d => d.PaymentDate);

        var items = articles.Select(a => new
        {
            a.Id,
            a.ExternalId,
            a.Title,
            a.Summary,
            a.OriginalUrl,
            a.Source,
            a.MarketCategory,
            a.Sentiment,
            a.ImportanceScore,
            a.MentionedTickersJson,
            a.IsModelValidated,
            a.IsRelevant,
            a.ValidationReasoning,
            a.PublishedAt,
            a.CreatedAt,
            DividendPaymentDate = paymentDatesByExternalId.TryGetValue(a.ExternalId, out var paymentDate)
                ? paymentDate
                : null
        });

        return Ok(new
        {
            totalItems,
            page,
            limit,
            items
        });
    }

    [HttpGet("symbol/{symbol}")]
    public async Task<IActionResult> GetNewsBySymbol(string symbol, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("Symbol is required.");

        var cleanSymbol = symbol.Trim().ToUpperInvariant();
        var articles = await _dbContext.NewsArticles
            .AsNoTracking()
            .Where(n => n.MentionedTickersJson.Contains(cleanSymbol) || n.Title.Contains(cleanSymbol))
            .OrderByDescending(n => n.PublishedAt)
            .Take(limit)
            .ToListAsync();

        var paymentDatesByExternalId = await _dbContext.Dividends
            .AsNoTracking()
            .ToDictionaryAsync(d => $"cse-dividend-{d.Id}", d => d.PaymentDate);

        var items = articles.Select(a => new
        {
            a.Id,
            a.ExternalId,
            a.Title,
            a.Summary,
            a.OriginalUrl,
            a.Source,
            a.MarketCategory,
            a.Sentiment,
            a.ImportanceScore,
            a.MentionedTickersJson,
            a.IsModelValidated,
            a.IsRelevant,
            a.ValidationReasoning,
            a.PublishedAt,
            a.CreatedAt,
            DividendPaymentDate = paymentDatesByExternalId.TryGetValue(a.ExternalId, out var paymentDate)
                ? paymentDate
                : null
        });

        return Ok(items);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetNewsStatus()
    {
        var totalArticles = await _dbContext.NewsArticles.CountAsync();
        var pendingValidation = await _dbContext.NewsArticles.CountAsync(n => !n.IsModelValidated);
        var validatedCount = await _dbContext.NewsArticles.CountAsync(n => n.IsModelValidated);
        var relevantCount = await _dbContext.NewsArticles.CountAsync(n => n.IsModelValidated && n.IsRelevant);

        return Ok(new
        {
            totalArticles,
            pendingValidation,
            validatedCount,
            relevantCount,
            isScanning = pendingValidation > 0,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncNews()
    {
        try
        {
            var count = await _newsIngestionService.IngestLatestNewsAsync();
            return Ok(new { message = $"Ingestion complete. Added {count} new validated articles.", count });
        }
        catch (Exception ex)
        {
            // The exception text can carry connection strings and provider internals, so it is
            // logged server-side and never returned to the caller.
            _logger.LogError(ex, "News ingestion failed.");
            return StatusCode(500, new { error = "News ingestion failed. See server logs for details." });
        }
    }
}
