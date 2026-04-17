using dotnetApp;
using Microsoft.Extensions.Hosting;

public class AlertJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AlertJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var alertService = scope.ServiceProvider.GetRequiredService<AlertService>();
                var stockService = scope.ServiceProvider.GetRequiredService<StockService>();

                await stockService.GetTradingSummaryAsync();
                await stockService.GetMarketStatus();
                await stockService.GetASPIData();
                await stockService.GetSnpData();
                await alertService.MonitorAlertsAsync(stoppingToken);

                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"AlertJob error: {ex}");
            }

        }
    }
}
