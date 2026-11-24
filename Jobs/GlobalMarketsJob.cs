using System;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Jobs;

/// <summary>
/// Keeps the "World markets" snapshot warm (US, Asia, gold/silver/oil, VIX/DXY) so the dashboard
/// never waits on the external feed. ~16 symbols every 5 minutes, shared by all users.
/// </summary>
public class GlobalMarketsJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GlobalMarketsJob> _logger;

    public GlobalMarketsJob(IServiceScopeFactory scopeFactory, ILogger<GlobalMarketsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting GlobalMarketsJob background service...");

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<GlobalMarketService>();
                await service.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing GlobalMarketsJob");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
