using System;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Jobs;

public class NewsValidationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NewsValidationJob> _logger;

    public NewsValidationJob(IServiceScopeFactory scopeFactory, ILogger<NewsValidationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting NewsValidationJob background service (always active)...");

        // Wait 15 seconds after startup before starting validation loop
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var validationService = scope.ServiceProvider.GetRequiredService<NewsValidationService>();

                int processedCount = await validationService.ValidatePendingArticlesAsync(stoppingToken);

                // If processed articles, short pause before checking next pending batch;
                // if no articles were pending, wait 10 seconds.
                if (processedCount > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NewsValidationJob loop");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
