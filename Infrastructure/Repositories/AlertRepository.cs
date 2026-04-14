using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Infrastructure.Repositories;

public class AlertRepository
{
    private readonly AppDbContext _db;
    public AlertRepository(AppDbContext db) => _db = db;


    public async Task<List<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken)
    {
        return await _db.Alerts
            .Where(alert => alert.IsActive)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAlertAsync(Alert alert)
    {
        _db.Alerts.Update(alert);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

}