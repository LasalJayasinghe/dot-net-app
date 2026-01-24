using dotnetApp;
using dotnetApp.Data;
using Microsoft.EntityFrameworkCore;

public class AlertService
{
    private readonly AppDbContext _dbContext;
    private readonly StockService _stockService;
    private readonly TelegramService _telegramService;
    public AlertService(AppDbContext _db, StockService stockService, TelegramService telegramService)
    {
        _dbContext = _db;
        _stockService = stockService;
        _telegramService = telegramService;
    }

    public async Task MonitorAlertsAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Monitoring alerts...");
        var alerts = await _dbContext.Alerts
            .Where(alert => !alert.IsActive)
            .ToListAsync(stoppingToken);

        foreach (var alert in alerts)
        {
            var existingStock = await _dbContext.Stocks.
                FirstOrDefaultAsync(s => s.Symbol == alert.Symbol, stoppingToken);

            if (existingStock == null)
            {
                Console.WriteLine($"Stock data for {alert.Symbol} not found.");
                continue;
            }

            var alertPriceCondition = alert.IsAbove ? existingStock.Price >= alert.TargetPrice : existingStock.Price <= alert.TargetPrice;
            if (alertPriceCondition)
            {
                try
                {
                    await _telegramService.SendMessageAsync(
                     $"Alert: {alert.Symbol} has reached the target price of {alert.TargetPrice}. Current price: {existingStock.Price}"
                 );

                    alert.IsActive = false;
                    _dbContext.Alerts.Update(alert);
                    await _dbContext.SaveChangesAsync(stoppingToken);

                    Console.WriteLine($"Alert triggered for {alert.Symbol} at price {existingStock.Price}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending alert for {alert.Symbol}: {ex.Message}");
                }
            }
        }
    }
}