using dotnetApp;
using Microsoft.Extensions.Hosting;

public class AlertJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan OpenPriceInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OpenMarketDataInterval = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ClosedStatusInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ClosedDataInterval = TimeSpan.FromMinutes(60);

    private readonly IServiceScopeFactory _scopeFactory;

    public AlertJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isOpen = false;
        var lastStatusCheck = DateTime.MinValue;
        var lastIndexUpdate = DateTime.MinValue;
        var lastPriceUpdate = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var alertService = scope.ServiceProvider.GetRequiredService<AlertService>();
                var stockService = scope.ServiceProvider.GetRequiredService<StockService>();

                var now = DateTime.UtcNow;

                // Status goes first so the checks below use the current market state.
                // A failed check leaves lastStatusCheck alone, so it retries next tick.
                if (now - lastStatusCheck >= (isOpen ? OpenMarketDataInterval : ClosedStatusInterval))
                {
                    if (await stockService.GetMarketStatus() != null)
                    {
                        isOpen = (await stockService.GetSavedMarketStatusAsync())?.IsOpen ?? false;
                        lastStatusCheck = now;
                    }
                }

                if (now - lastIndexUpdate >= (isOpen ? OpenMarketDataInterval : ClosedDataInterval))
                {
                    await stockService.GetASPIData();
                    await stockService.GetSnpData();
                    lastIndexUpdate = now;
                }

                // Alerts compare against saved prices, so the summary must run first.
                if (now - lastPriceUpdate >= (isOpen ? OpenPriceInterval : ClosedDataInterval))
                {
                    await stockService.GetTradingSummaryAsync();
                    await alertService.MonitorAlertsAsync(stoppingToken);
                    lastPriceUpdate = now;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AlertJob error: {ex}");
            }

            // Outside the try so an error can't skip the wait and spin the loop.
            await Task.Delay(TickInterval, stoppingToken);
        }
    }
}
