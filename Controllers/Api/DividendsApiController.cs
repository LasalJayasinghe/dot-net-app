using System.Linq;
using System.Threading.Tasks;
using dotnetApp.Application.Dtos;
using dotnetApp.Domain.Common;
using dotnetApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/dividends")]
public class DividendsApiController : ControllerBase
{
    private const int MaxLimit = 100;

    private readonly AppDbContext _dbContext;

    public DividendsApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("symbol/{symbol}")]
    public async Task<IActionResult> GetBySymbol(string symbol, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("Symbol is required.");

        var cleanSymbol = NormalizeSymbol(symbol);

        var items = await _dbContext.Dividends
            .AsNoTracking()
            .Where(d => d.BaseSymbol == cleanSymbol)
            .OrderByDescending(d => d.RecordDate)
            .ThenByDescending(d => d.DateOfAnnouncement)
            .Take(Clamp(limit))
            .Select(DividendDto.Projection)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming([FromQuery] int limit = 20)
    {
        // RecordDate holds a Colombo calendar date, so it must be compared against the Colombo
        // date - using DateTime.UtcNow.Date would drop entries a day before their record date.
        var today = ColomboTime.Today;

        var items = await _dbContext.Dividends
            .AsNoTracking()
            .Where(d => d.RecordDate >= today)
            .OrderBy(d => d.RecordDate)
            .ThenByDescending(d => d.DateOfAnnouncement)
            .Take(Clamp(limit))
            .Select(DividendDto.Projection)
            .ToListAsync();

        return Ok(items);
    }

    private static int Clamp(int limit) => limit < 1 ? 1 : limit > MaxLimit ? MaxLimit : limit;

    /// <summary>Strips any CSE class suffix so "CSLK.N0000" and "cslk" both match BaseSymbol "CSLK".</summary>
    private static string NormalizeSymbol(string symbol)
    {
        var trimmed = symbol.Trim().ToUpperInvariant();
        var dot = trimmed.IndexOf('.');
        return dot >= 0 ? trimmed[..dot] : trimmed;
    }
}
