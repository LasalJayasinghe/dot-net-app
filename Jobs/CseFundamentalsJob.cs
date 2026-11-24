using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Services.Fundamentals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Jobs;

/// <summary>
/// Keeps CSE fundamentals current:
///   • every cycle, reads interim statements filed since the last check (cse.lk getFinancialAnnouncement);
///   • until every listed company has been read once, backfills a few companies per cycle.
/// Requests are spaced out — cse.lk is a shared public site and the PDFs run to ~1 MB.
/// </summary>
public class CseFundamentalsJob : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan BackfillInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BetweenCompanies = TimeSpan.FromSeconds(5);
    private const int BackfillPerCycle = 5;

    /// <summary>Companies already tried in this process (some have no interim reports listed) — not retried until restart.</summary>
    private static readonly ConcurrentDictionary<string, byte> Attempted = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CseFundamentalsJob> _logger;

    public CseFundamentalsJob(IServiceScopeFactory scopeFactory, ILogger<CseFundamentalsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting CseFundamentalsJob background service...");

        // Let startup (migrations, other jobs' first runs) settle before pulling PDFs.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var backlog = false;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CseFundamentalsService>();

                // 1. New filings.
                foreach (var symbol in await service.GetSymbolsWithNewFilingsAsync(stoppingToken))
                {
                    var read = await service.SyncSymbolAsync(symbol, stoppingToken);
                    if (read > 0) _logger.LogInformation("Fundamentals: read {Count} new report(s) for {Symbol}", read, symbol);
                    await Task.Delay(BetweenCompanies, stoppingToken);
                }

                // 2. One-time backfill, a few companies per cycle.
                var pending = (await service.GetUnsyncedSymbolsAsync(stoppingToken))
                    .FindAll(s => !Attempted.ContainsKey(s));
                backlog = pending.Count > BackfillPerCycle;
                foreach (var symbol in pending.GetRange(0, Math.Min(BackfillPerCycle, pending.Count)))
                {
                    Attempted.TryAdd(symbol, 0);
                    await service.SyncSymbolAsync(symbol, stoppingToken);
                    await Task.Delay(BetweenCompanies, stoppingToken);
                }
                if (pending.Count > 0)
                    _logger.LogInformation("Fundamentals backfill: {Remaining} companies left", Math.Max(0, pending.Count - BackfillPerCycle));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing CseFundamentalsJob");
            }

            await Task.Delay(backlog ? BackfillInterval : IdleInterval, stoppingToken);
        }
    }
}
