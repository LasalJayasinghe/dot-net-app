using dotnetApp;
using dotnetApp.Application.Interface;
using dotnetApp.Application.Services;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>Which alerts a monitoring pass covers.</summary>
public enum AlertScope
{
    /// <summary>CSE stocks — checked on the CSE price schedule (every 2 min while open).</summary>
    Cse,
    /// <summary>Indices, crypto and USDT/LKR — checked every 2 min, 24/7.</summary>
    Markets,
}

public class AlertService
{
    private readonly AppDbContext _dbContext;
    private readonly MarketPriceResolver _prices;
    private readonly TelegramService _telegramService;
    private readonly IPushNotificationService _pushService;

    public AlertService(
        AppDbContext _db,
        MarketPriceResolver prices,
        TelegramService telegramService,
        IPushNotificationService pushService)
    {
        _dbContext = _db;
        _prices = prices;
        _telegramService = telegramService;
        _pushService = pushService;
    }

    /// <summary>
    /// Sends Telegram + push for every active alert whose condition holds. Alerts stay active, so an alert
    /// keeps notifying on each pass while the condition holds (intended behaviour).
    /// </summary>
    public async Task MonitorAlertsAsync(AlertScope scope, CancellationToken stoppingToken)
    {
        var alerts = (await _dbContext.Alerts
                .AsNoTracking()
                .Where(alert => alert.IsActive)
                .ToListAsync(stoppingToken))
            .Where(a => MarketPriceResolver.IsMarketSymbol(a.Symbol) == (scope == AlertScope.Markets))
            .ToList();

        if (alerts.Count == 0) return;

        var prices = await _prices.GetPricesAsync(alerts.Select(a => a.Symbol), stoppingToken);
        var userIds = alerts.Select(a => a.CreatedBy).Distinct().ToList();

        var profiles = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, stoppingToken);

        foreach (var alert in alerts)
        {
            if (!prices.TryGetValue(alert.Symbol, out var price))
            {
                Console.WriteLine($"Price for alert symbol {alert.Symbol} not available.");
                continue;
            }

            var triggered = alert.IsAbove ? price >= alert.TargetPrice : price <= alert.TargetPrice;
            if (!triggered) continue;

            try
            {
                var label = MarketPriceResolver.LabelOf(alert.Symbol);
                var target = MarketPriceResolver.FormatPrice(alert.Symbol, alert.TargetPrice);
                var now = MarketPriceResolver.FormatPrice(alert.Symbol, price);
                var message = $"Alert: {label} has reached the target of {target}. Current: {now}";

                if (profiles.TryGetValue(alert.CreatedBy, out var userProfile) &&
                    long.TryParse(userProfile.TelegramId, out long userChatId) &&
                    userChatId != 0)
                {
                    await _telegramService.SendMessageAsync(userChatId, message);
                }

                // The phone gets the same alert. Unlike Telegram this needs no linked account.
                // Stock alerts use type "price-alert" (the mobile app deep-links to the stock page);
                // index/crypto/FX alerts use "market-alert" so the app doesn't open a stock page for them.
                var isStock = !MarketPriceResolver.IsMarketSymbol(alert.Symbol);
                await _pushService.SendToUserAsync(
                    alert.CreatedBy,
                    $"{label} hit {target}",
                    $"Now at {now}. Your {(alert.IsAbove ? "above" : "below")} alert has triggered.",
                    new Dictionary<string, object?>
                    {
                        ["type"] = isStock ? "price-alert" : "market-alert",
                        ["alertId"] = alert.Id,
                        ["symbol"] = alert.Symbol,
                        ["assetType"] = MarketPriceResolver.AssetTypeOf(alert.Symbol).ToString(),
                        ["price"] = price,
                        ["targetPrice"] = alert.TargetPrice,
                    },
                    stoppingToken);

                Console.WriteLine($"Alert triggered for {alert.Symbol} at {now}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending alert for {alert.Symbol}: {ex.Message}");
            }
        }
    }
}
