using System.Globalization;
using System.Text;
using dotnetApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

/// <summary>
/// Admin view of the CSE price-history recorder (<see cref="Jobs.CseHistoryJob"/> runs everything
/// automatically; these endpoints are for checking it and for re-running a task by hand).
/// </summary>
[ApiController]
[Route("api/admin/cse-history")]
[Authorize(Roles = "Admin")]
public class CseHistoryAdminController : ControllerBase
{
    private readonly CseHistoryService _history;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<CseHistoryAdminController> _logger;

    public CseHistoryAdminController(CseHistoryService history, IServiceScopeFactory scopes,
        IHostApplicationLifetime lifetime, ILogger<CseHistoryAdminController> logger)
    {
        _history = history;
        _scopes = scopes;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>Row counts, date range, last runs.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _history.GetSummaryAsync(ct));

    /// <summary>Re-runs the chart backfill in the background. period 5 ≈ 1 year, 4 ≈ 2 months. Safe to repeat.</summary>
    [HttpPost("backfill")]
    public IActionResult Backfill([FromQuery] int period = 5)
    {
        if (CseHistoryService.IsBusy) return Conflict(new { message = "A history task is already running." });
        RunInBackground("backfill", (svc, ct) => svc.BackfillAsync(period, ct));
        return Accepted(new { message = $"Backfill (period {(period >= 5 ? 5 : 4)}) started. Check GET status for the result." });
    }

    /// <summary>Records the latest finished session now (normally done every 15 min after the close).</summary>
    [HttpPost("record")]
    public async Task<IActionResult> Record(CancellationToken ct)
    {
        if (CseHistoryService.IsBusy) return Conflict(new { message = "A history task is already running." });
        return Ok(await _history.RecordLatestSessionAsync(ct));
    }

    /// <summary>CSV export for backtests. Dates are yyyy-MM-dd; symbol is optional (e.g. JKH or JKH.N0000).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? symbol,
        CancellationToken ct)
    {
        DateOnly? f = null, t = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fd))
                return BadRequest(new { message = "from must be yyyy-MM-dd." });
            f = fd;
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var td))
                return BadRequest(new { message = "to must be yyyy-MM-dd." });
            t = td;
        }

        var rows = await _history.GetRowsAsync(f, t, symbol, ct);
        var sb = new StringBuilder("symbol,date,open,high,low,close,previous_close,share_volume,trades,turnover,market_cap,source\n");
        foreach (var r in rows)
        {
            sb.Append(r.Symbol).Append(',')
              .Append(r.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
              .Append(N(r.Open)).Append(',').Append(N(r.High)).Append(',').Append(N(r.Low)).Append(',')
              .Append(N(r.Close)).Append(',').Append(N(r.PreviousClose)).Append(',')
              .Append(r.ShareVolume.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Trades?.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(N(r.Turnover)).Append(',').Append(N(r.MarketCap)).Append(',')
              .Append(r.Source).Append('\n');
        }

        var name = $"cse-prices{(string.IsNullOrWhiteSpace(symbol) ? "" : "-" + CseSymbols.ToLookup(symbol))}" +
                   $"{(f is null ? "" : "-from-" + f.Value.ToString("yyyyMMdd"))}{(t is null ? "" : "-to-" + t.Value.ToString("yyyyMMdd"))}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", name);

        static string N(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private void RunInBackground(string task, Func<CseHistoryService, CancellationToken, Task> work)
    {
        var ct = _lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await work(scope.ServiceProvider.GetRequiredService<CseHistoryService>(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "CSE history {Task} (admin-triggered) failed.", task);
            }
        }, ct);
    }
}
