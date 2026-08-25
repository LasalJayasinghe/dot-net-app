using System;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace dotnetApp.Jobs;

public class NewsIngestionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NewsIngestionJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 10 seconds after startup before initial run
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var newsService = scope.ServiceProvider.GetRequiredService<NewsIngestionService>();

                await newsService.IngestLatestNewsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NewsIngestionJob] Error in news background execution: {ex.Message}");
            }

            // Run every 30 minutes
            await Task.Delay(TimeSpan.FromMinutes(45), stoppingToken);
        }
    }
}
