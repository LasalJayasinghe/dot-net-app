using dotnetApp;
using dotnetApp.Infrastructure.Data;
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
            .Where(alert => alert.IsActive)
            .ToListAsync(stoppingToken);

        if (!alerts.Any()) return;

        var symbols = alerts.Select(a => a.Symbol).Distinct().ToList();
        var userIds = alerts.Select(a => a.CreatedBy).Distinct().ToList();

        var stocks = await _dbContext.Stocks
            .Where(s => symbols.Contains(s.Symbol))
            .ToDictionaryAsync(s => s.Symbol, stoppingToken);

        var profiles = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, stoppingToken);

        bool hasChanges = false;

        foreach (var alert in alerts)
        {
            if (!stocks.TryGetValue(alert.Symbol, out var existingStock))
            {
                Console.WriteLine($"Stock data for {alert.Symbol} not found.");
                continue;
            }

            var alertPriceCondition = alert.IsAbove ? existingStock.Price >= alert.TargetPrice : existingStock.Price <= alert.TargetPrice;
            if (alertPriceCondition)
            {
                try
                {
                    if (profiles.TryGetValue(alert.CreatedBy, out var userProfile) && 
                        long.TryParse(userProfile.TelegramId, out long userChatId) && 
                        userChatId != 0)
                    {
                        await _telegramService.SendMessageAsync(
                            userChatId, 
                            $"Alert: {alert.Symbol} has reached the target price of {alert.TargetPrice:N2}. Current price: {existingStock.Price:N2}"
                        );
                    }

                    alert.IsActive = false;
                    _dbContext.Alerts.Update(alert);
                    hasChanges = true;

                    Console.WriteLine($"Alert triggered for {alert.Symbol} at price {existingStock.Price:N2}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending alert for {alert.Symbol}: {ex.Message}");
                }
            }
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}