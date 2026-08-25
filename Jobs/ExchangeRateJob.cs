using System;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Jobs;

public class ExchangeRateJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExchangeRateJob> _logger;

    public ExchangeRateJob(IServiceScopeFactory scopeFactory, ILogger<ExchangeRateJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting ExchangeRateJob background service...");

        // Initial delay of 5 seconds on startup
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var p2pService = scope.ServiceProvider.GetRequiredService<BinanceP2pService>();

                await p2pService.FetchAndUpdateUsdtLkrRateAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing ExchangeRateJob");
            }

            // Sync once a day (every 24 hours)
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
