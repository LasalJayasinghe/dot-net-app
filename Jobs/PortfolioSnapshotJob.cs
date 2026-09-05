using System;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Jobs;

/// <summary>
/// Records an hourly valuation of every user's portfolios so the dashboard can graph
/// how total portfolio value moves over time. Captures run on the top of each hour,
/// which keeps the x-axis evenly spaced regardless of when the app was started.
/// </summary>
public class PortfolioSnapshotJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// Gives the live price feeds (Binance socket, CSE polling job) time to warm up
    /// before the first capture, so the opening data point is not a false zero.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PortfolioSnapshotJob> _logger;

    public PortfolioSnapshotJob(IServiceScopeFactory scopeFactory, ILogger<PortfolioSnapshotJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PortfolioSnapshotJob started — capturing portfolio value every hour.");

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await CaptureAsync(stoppingToken);

            // Align to the top of the next hour, then tick hourly from there.
            var now       = DateTime.UtcNow;
            var nextHour  = PortfolioSnapshotService.TruncateToHour(now).AddHours(1);
            var untilHour = nextHour - now;
            if (untilHour > TimeSpan.Zero)
                await Task.Delay(untilHour, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CaptureAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task CaptureAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<PortfolioSnapshotService>();

            var written = await service.CaptureAllAsync(ct);
            _logger.LogInformation("Portfolio snapshot cycle complete — wrote {Count} row(s).", written);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running portfolio snapshot cycle.");
        }
    }
}
