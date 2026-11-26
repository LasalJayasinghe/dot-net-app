using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Services.Desk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

/// <summary>
/// The trading desk.
///   GET /api/desk/overview?market=cse|crypto   — market panel, full board, positions, watchlist, alerts, today
///   GET /api/desk/symbol/{symbol}?market=…      — the selected-stock panel
/// Served from caches (prices refresh every ~30 s while the CSE is open; crypto is live).
/// </summary>
[ApiController]
[Route("api/desk")]
[Authorize]
public class DeskApiController : ControllerBase
{
    private readonly DeskService _desk;
    private readonly UserManager<ApplicationUser> _userManager;

    public DeskApiController(DeskService desk, UserManager<ApplicationUser> userManager)
    {
        _desk = desk;
        _userManager = userManager;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<DeskOverviewDto>> Overview([FromQuery] string? market, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();
        return Ok(await _desk.GetOverviewAsync(userId, market, ct));
    }

    [HttpGet("symbol/{symbol}")]
    public async Task<ActionResult<DeskSymbolDto>> Symbol(string symbol, [FromQuery] string? market, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { message = "Symbol is required." });
        return Ok(await _desk.GetSymbolAsync(userId, symbol, market, ct));
    }
}
