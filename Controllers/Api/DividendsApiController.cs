using System;
using System.Linq;
using System.Threading.Tasks;
using dotnetApp.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/dividends")]
public class DividendsApiController : ControllerBase
{
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

        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        var cleanSymbol = symbol.Trim().ToUpperInvariant();

        var items = await _dbContext.Dividends
            .AsNoTracking()
            .Where(d => d.Symbol == cleanSymbol || d.Symbol.StartsWith(cleanSymbol + "."))
            .OrderByDescending(d => d.RecordDate)
            .ThenByDescending(d => d.DateOfAnnouncement)
            .Take(limit)
            .ToListAsync();

        return Ok(items);
    }
}
