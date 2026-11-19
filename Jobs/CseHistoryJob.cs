using dotnetApp.Application.Services;

namespace dotnetApp.Jobs;

/// <summary>
/// Builds the permanent CSE daily price history (see <see cref="CseHistoryService"/>):
///   • on startup: 1-year backfill if the table is empty, otherwise a 2-month gap repair
///     (covers any trading days missed while the app was down);
///   • every 15 minutes outside market hours: records the latest finished session
///     (a no-op once that session is stored, so holidays and repeats are harmless);
///   • every Sunday: 2-month gap repair.
/// No AI or manual step is involved.
/// </summary>
public class CseHistoryJob : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    // CSE trades 09:30–14:30 Colombo time; closing prices are published shortly after.
    private static readonly TimeSpan SessionStart = new(9, 15, 0);
    private static readonly TimeSpan SessionEnd = new(14, 40, 0);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CseHistoryJob> _logger;
    private DateOnly _lastRepair;

    public CseHistoryJob(IServiceScopeFactory scopes, ILogger<CseHistoryJob> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await StartupAsync(stoppingToken);

            using var timer = new PeriodicTimer(Tick);
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "CSE history job tick failed.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task StartupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var history = scope.ServiceProvider.GetRequiredService<CseHistoryService>();

            if (!await history.HasPriceDataAsync(ct))
            {
                _logger.LogInformation("CSE history: table empty, starting the 1-year backfill.");
                await history.BackfillAsync(period: 5, ct);
            }
            else
            {
                await history.BackfillAsync(period: 4, ct);
            }
            _lastRepair = CseHistoryService.TodayInColombo();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "CSE history startup backfill failed; the weekly repair will retry.");
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CseHistoryService.Colombo);
        var today = DateOnly.FromDateTime(now);
        var weekday = now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        using var scope = _scopes.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<CseHistoryService>();

        // During the session closing prices aren't published, so there is nothing to record.
        if (!(weekday && now.TimeOfDay >= SessionStart && now.TimeOfDay < SessionEnd))
            await history.RecordLatestSessionAsync(ct);

        if (now.DayOfWeek == DayOfWeek.Sunday && now.Hour >= 3 && _lastRepair != today)
        {
            await history.BackfillAsync(period: 4, ct);
            _lastRepair = today;
        }
    }
}
