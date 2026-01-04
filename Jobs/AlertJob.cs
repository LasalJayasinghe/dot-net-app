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
            using var scope = _scopeFactory.CreateScope();

            var alertService = scope.ServiceProvider.GetRequiredService<AlertService>();

            await alertService.MonitorAlertsAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
