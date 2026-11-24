using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Services.Fundamentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Authorize]
public class FundamentalsApiController : ControllerBase
{
    private readonly CseFundamentalsService _fundamentals;

    public FundamentalsApiController(CseFundamentalsService fundamentals)
    {
        _fundamentals = fundamentals;
    }

    /// <summary>
    /// NAV per share, P/BV, trailing EPS, P/E, ROE, dividend yield, public float, beta and 52-week range.
    /// Each figure carries a status (verified / unverified / manual / partial / missing) and a note.
    /// </summary>
    [HttpGet("api/stocks/{symbol}/fundamentals")]
    public async Task<ActionResult<StockFundamentalsDto>> Get(string symbol, CancellationToken ct) =>
        Ok(await _fundamentals.GetFundamentalsAsync(symbol, ct));

    /// <summary>Extraction hit rate across stored reports.</summary>
    [HttpGet("api/admin/cse-fundamentals/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _fundamentals.GetStatusAsync(ct));

    /// <summary>Reads a company's newest reports now instead of waiting for the job.</summary>
    [HttpPost("api/admin/cse-fundamentals/{symbol}/sync")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Sync(string symbol, CancellationToken ct) =>
        Ok(new { read = await _fundamentals.SyncSymbolAsync(symbol, ct) });

    /// <summary>Hand-corrects the newest report's NAV / EPS when the PDF couldn't be read reliably.</summary>
    [HttpPut("api/admin/cse-fundamentals/{symbol}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Override(string symbol, [FromBody] FundamentalsOverrideDto input, CancellationToken ct) =>
        await _fundamentals.OverrideLatestAsync(symbol, input, ct)
            ? NoContent()
            : NotFound(new { message = "No report stored for this symbol yet." });
}
