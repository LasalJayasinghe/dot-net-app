using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/markets")]
public class GlobalMarketsApiController : ControllerBase
{
    private readonly GlobalMarketService _globalMarketService;

    public GlobalMarketsApiController(GlobalMarketService globalMarketService)
    {
        _globalMarketService = globalMarketService;
    }

    /// <summary>
    /// US close + futures, Asian indices, gold/silver/oil and VIX/DXY with a one-line mood read.
    /// Served from a shared cache refreshed every 5 minutes.
    /// </summary>
    [HttpGet("global")]
    public async Task<ActionResult<GlobalMarketOverviewDto>> GetGlobal(CancellationToken cancellationToken)
    {
        return Ok(await _globalMarketService.GetOverviewAsync(cancellationToken));
    }
}
