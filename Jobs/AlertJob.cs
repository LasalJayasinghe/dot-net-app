using dotnetApp;
using dotnetApp.Application.Services.Desk;
using Microsoft.Extensions.Hosting;

public class AlertJob : BackgroundService
{
    // Short tick so the open-market price cadence (default 30 s) is honoured.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    /// <summary>Default for Cse:OpenPollSeconds — how often prices and indices refresh while the market is open.</summary>
    private const int DefaultOpenPollSeconds = 30;
    private static readonly TimeSpan OpenMarketDataInterval = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ClosedStatusInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ClosedDataInterval = TimeSpan.FromMinutes(60);

    // Index / crypto / USDT-LKR alerts run 24/7 (crypto never closes) at the same 2-minute cadence.
    private static readonly TimeSpan MarketAlertInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MarketIntradayStore _intraday;

    /// <summary>
    /// Price + index refresh while the market is open (one tradeSummary call covers every stock).
    /// Raise Cse:OpenPollSeconds if cse.lk ever throttles us; never below 15 s.
    /// </summary>
    private readonly TimeSpan _openPriceInterval;

    public AlertJob(IServiceScopeFactory scopeFactory, MarketIntradayStore intraday, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _intraday = intraday;
        _openPriceInterval = TimeSpan.FromSeconds(
            Math.Max(15, configuration.GetValue("Cse:OpenPollSeconds", DefaultOpenPollSeconds)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isOpen = false;
        var lastStatusCheck = DateTime.MinValue;
        var lastIndexUpdate = DateTime.MinValue;
        var lastPriceUpdate = DateTime.MinValue;
        var lastMarketAlertCheck = DateTime.MinValue;

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

                // While open, indices refresh with prices so the desk's intraday ASPI line is fine-grained.
                if (now - lastIndexUpdate >= (isOpen ? _openPriceInterval : ClosedDataInterval))
                {
                    var aspi = await stockService.GetASPIData();
                    var snp = await stockService.GetSnpData();
                    if (isOpen && aspi != null) _intraday.Append(aspi.value, snp.value);
                    lastIndexUpdate = now;
                }

                // Alerts compare against saved prices, so the summary must run first.
                if (now - lastPriceUpdate >= (isOpen ? _openPriceInterval : ClosedDataInterval))
                {
                    await stockService.GetTradingSummaryAsync();
                    await alertService.MonitorAlertsAsync(AlertScope.Cse, stoppingToken);
                    lastPriceUpdate = now;
                }

                // Indices are refreshed above; crypto comes from the live Binance feed.
                if (now - lastMarketAlertCheck >= MarketAlertInterval)
                {
                    await alertService.MonitorAlertsAsync(AlertScope.Markets, stoppingToken);
                    lastMarketAlertCheck = now;
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
