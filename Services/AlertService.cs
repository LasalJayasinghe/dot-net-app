using dotnetApp.Data;
using Microsoft.EntityFrameworkCore;

public class AlertService
{
    private readonly AppDbContext _dbContext;
    public AlertService(AppDbContext _db)
    {
        _dbContext = _db;
    }

    public async Task MonitorAlertsAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Monitoring alerts...");
        var alerts = await _dbContext.Alerts.ToListAsync(stoppingToken);
        foreach (var alert in alerts)
        {
            Console.WriteLine($"Monitoring alert: {alert.Id} for {alert.Symbol} at {alert.TargetPrice}");
        }
    }
} 