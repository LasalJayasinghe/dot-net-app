using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Application.Services;

/// <summary>
/// Captures hourly point-in-time valuations of every user's portfolios so the UI can
/// graph how total portfolio value moves over time, and reads those series back.
/// </summary>
public class PortfolioSnapshotService
{
    /// <summary>Snapshots older than this are pruned on each capture cycle.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);

    private readonly AppDbContext _db;
    private readonly PortfolioService _portfolioService;
    private readonly ILogger<PortfolioSnapshotService> _logger;

    public PortfolioSnapshotService(
        AppDbContext db,
        PortfolioService portfolioService,
        ILogger<PortfolioSnapshotService> logger)
    {
        _db               = db;
        _portfolioService = portfolioService;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Capture
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes one snapshot per portfolio, plus one aggregated net-worth snapshot,
    /// for every user that owns at least one portfolio. Idempotent within the hour:
    /// re-running overwrites the current hour's rows rather than duplicating them.
    /// </summary>
    public async Task<int> CaptureAllAsync(CancellationToken ct = default)
    {
        var bucket = TruncateToHour(DateTime.UtcNow);

        var userIds = await _db.Portfolios
            .AsNoTracking()
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(ct);

        var written = 0;
        foreach (var userId in userIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                written += await CaptureUserAsync(userId, bucket, ct);
            }
            catch (Exception ex)
            {
                // One bad user must not abort the whole cycle.
                _logger.LogError(ex, "Failed to snapshot portfolios for user {UserId}", userId);
            }
        }

        await PruneAsync(ct);
        return written;
    }

    /// <summary>Captures a single user's snapshots for the given hour bucket.</summary>
    public async Task<int> CaptureUserAsync(string userId, DateTime bucket, CancellationToken ct = default)
    {
        var netWorth = await _portfolioService.GetNetWorthAsync(userId);
        if (netWorth.Portfolios.Count == 0) return 0;

        var usdtToLkr = netWorth.UsdtToLkrRate;
        var lkrToUsdt = netWorth.LkrToUsdtRate;
        if (lkrToUsdt == 0 && usdtToLkr > 0) lkrToUsdt = 1m / usdtToLkr;

        var written = 0;
        var skipped = 0;

        foreach (var p in netWorth.Portfolios)
        {
            // A portfolio that holds something but prices at zero means the price feed
            // was unavailable (e.g. the Binance socket has not warmed up yet). Recording
            // that would punch a false crash into the graph, so skip it this cycle.
            if (p.HoldingCount > 0 && p.TotalValue <= 0)
            {
                skipped++;
                continue;
            }

            var toLkr  = ConversionRate(p.BaseCurrency, "LKR", usdtToLkr, lkrToUsdt);
            var toUsdt = ConversionRate(p.BaseCurrency, "USDT", usdtToLkr, lkrToUsdt);

            await UpsertAsync(new PortfolioSnapshot
            {
                UserId             = userId,
                PortfolioId        = p.Id,
                CapturedAt         = bucket,
                TotalValueLkr      = p.TotalValue * toLkr,
                TotalValueUsdt     = p.TotalValue * toUsdt,
                TotalProfitLossLkr = p.TotalProfitLoss * toLkr,
                TotalProfitLossUsdt = p.TotalProfitLoss * toUsdt,
                UsdtToLkrRate      = usdtToLkr,
                HoldingCount       = p.HoldingCount
            }, ct);
            written++;
        }

        // Only record the aggregate when every portfolio priced successfully — a partial
        // total would look like a sudden drop in net worth rather than a missing feed.
        if (skipped == 0)
        {
            await UpsertAsync(new PortfolioSnapshot
            {
                UserId              = userId,
                PortfolioId         = null,
                CapturedAt          = bucket,
                TotalValueLkr       = netWorth.TotalNetWorthLkr,
                TotalValueUsdt      = netWorth.TotalNetWorthUsdt,
                TotalProfitLossLkr  = netWorth.TotalProfitLossLkr,
                TotalProfitLossUsdt = netWorth.TotalProfitLossUsdt,
                UsdtToLkrRate       = usdtToLkr,
                HoldingCount        = netWorth.Portfolios.Sum(p => p.HoldingCount)
            }, ct);
            written++;
        }
        else
        {
            _logger.LogWarning(
                "Skipped net-worth snapshot for user {UserId}: {Count} portfolio(s) priced at zero.",
                userId, skipped);
        }

        await _db.SaveChangesAsync(ct);
        return written;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the value series for a user's net worth (portfolioId == null) or a single
    /// portfolio. Long ranges are down-sampled to one point per day (the day's last
    /// snapshot) to keep the payload small.
    /// </summary>
    public async Task<PortfolioHistoryDto> GetHistoryAsync(
        string userId, int? portfolioId, string range, CancellationToken ct = default)
    {
        var normalized = (range ?? "1M").Trim().ToUpperInvariant();
        var window     = RangeToWindow(normalized);
        var daily      = normalized is "3M" or "1Y" or "ALL";

        var query = _db.PortfolioSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId);

        query = portfolioId.HasValue
            ? query.Where(s => s.PortfolioId == portfolioId.Value)
            : query.Where(s => s.PortfolioId == null);

        if (window.HasValue)
        {
            var from = DateTime.UtcNow - window.Value;
            query = query.Where(s => s.CapturedAt >= from);
        }

        var rows = await query.OrderBy(s => s.CapturedAt).ToListAsync(ct);

        if (daily)
        {
            rows = rows
                .GroupBy(s => s.CapturedAt.Date)
                .Select(g => g.OrderBy(s => s.CapturedAt).Last())
                .OrderBy(s => s.CapturedAt)
                .ToList();
        }

        var points = rows.Select(s => new PortfolioHistoryPointDto
        {
            CapturedAt      = DateTime.SpecifyKind(s.CapturedAt, DateTimeKind.Utc),
            ValueLkr        = Math.Round(s.TotalValueLkr, 4),
            ValueUsdt       = Math.Round(s.TotalValueUsdt, 4),
            ProfitLossLkr   = Math.Round(s.TotalProfitLossLkr, 4),
            ProfitLossUsdt  = Math.Round(s.TotalProfitLossUsdt, 4)
        }).ToList();

        var start = points.Count > 0 ? points[0].ValueLkr : 0m;
        var end   = points.Count > 0 ? points[^1].ValueLkr : 0m;

        return new PortfolioHistoryDto
        {
            Range          = normalized,
            PortfolioId    = portfolioId,
            Interval       = daily ? "day" : "hour",
            StartValueLkr  = start,
            EndValueLkr    = end,
            ChangeLkr      = Math.Round(end - start, 4),
            ChangePercent  = start > 0 ? Math.Round(((end - start) / start) * 100, 2) : 0m,
            Points         = points
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(PortfolioSnapshot snapshot, CancellationToken ct)
    {
        var sameBucket = _db.PortfolioSnapshots
            .Where(s => s.UserId == snapshot.UserId && s.CapturedAt == snapshot.CapturedAt);

        // Match the aggregate row explicitly on IS NULL rather than against a null
        // parameter, which SQL would never match.
        sameBucket = snapshot.PortfolioId.HasValue
            ? sameBucket.Where(s => s.PortfolioId == snapshot.PortfolioId.Value)
            : sameBucket.Where(s => s.PortfolioId == null);

        var existing = await sameBucket.FirstOrDefaultAsync(ct);

        if (existing == null)
        {
            _db.PortfolioSnapshots.Add(snapshot);
            return;
        }

        existing.TotalValueLkr       = snapshot.TotalValueLkr;
        existing.TotalValueUsdt      = snapshot.TotalValueUsdt;
        existing.TotalProfitLossLkr  = snapshot.TotalProfitLossLkr;
        existing.TotalProfitLossUsdt = snapshot.TotalProfitLossUsdt;
        existing.UsdtToLkrRate       = snapshot.UsdtToLkrRate;
        existing.HoldingCount        = snapshot.HoldingCount;
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        var cutoff  = DateTime.UtcNow - Retention;
        var deleted = await _db.PortfolioSnapshots
            .Where(s => s.CapturedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Pruned {Count} portfolio snapshot(s) older than {Cutoff:u}.", deleted, cutoff);
    }

    public static DateTime TruncateToHour(DateTime utc)
        => new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

    private static TimeSpan? RangeToWindow(string range) => range switch
    {
        "1D"  => TimeSpan.FromDays(1),
        "1W"  => TimeSpan.FromDays(7),
        "1M"  => TimeSpan.FromDays(30),
        "3M"  => TimeSpan.FromDays(90),
        "1Y"  => TimeSpan.FromDays(365),
        "ALL" => null,
        _     => TimeSpan.FromDays(30)
    };

    private static decimal ConversionRate(string fromCurrency, string toCurrency, decimal usdtToLkr, decimal lkrToUsdt)
    {
        if (fromCurrency == toCurrency) return 1m;
        if (fromCurrency == "USDT" && toCurrency == "LKR") return usdtToLkr;
        if (fromCurrency == "LKR"  && toCurrency == "USDT") return lkrToUsdt;
        return 1m;
    }
}
