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

[ApiController]
[Route("api/news")]
public class NewsApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly NewsIngestionService _newsIngestionService;

    public NewsApiController(AppDbContext dbContext, NewsIngestionService newsIngestionService)
    {
        _dbContext = dbContext;
        _newsIngestionService = newsIngestionService;
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

        return Ok(new
        {
            totalItems,
            page,
            limit,
            items = articles
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

        return Ok(articles);
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
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
