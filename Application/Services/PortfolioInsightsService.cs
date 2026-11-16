using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Domain.Common;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Application.Services;

/// <summary>
/// Decision support for a stock portfolio: rule-based signals per holding (from real CSE daily prices)
/// and announced dividends for the stocks held. Kept separate from the portfolio detail so the holdings
/// table renders immediately; history is cached by <see cref="StockService"/> for 30 minutes.
/// </summary>
public class PortfolioInsightsService
{
    private const decimal NearExtremeThreshold = 0.05m; // within 5% of the 52-week high/low
    private const decimal VolumeSpikeRatio = 2m;

    private readonly AppDbContext _db;
    private readonly StockService _stocks;
    private readonly ILogger<PortfolioInsightsService> _logger;

    public PortfolioInsightsService(AppDbContext db, StockService stocks, ILogger<PortfolioInsightsService> logger)
    {
        _db = db;
        _stocks = stocks;
        _logger = logger;
    }

    public async Task<PortfolioInsightsDto?> GetInsightsAsync(string userId, int portfolioId, CancellationToken ct = default)
    {
        var portfolio = await _db.Portfolios
            .AsNoTracking()
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId, ct);
        if (portfolio == null) return null;

        var result = new PortfolioInsightsDto { PortfolioId = portfolioId, GeneratedAt = DateTime.UtcNow };
        if (portfolio.Type != PortfolioType.Stocks) return result; // signals/dividends are CSE-only

        var holdings = portfolio.Holdings
            .GroupBy(h => CseSymbols.ToLookup(h.Symbol))
            .Select(g => (Symbol: g.Key, Quantity: g.Sum(h => h.Quantity)))
            .ToList();

        // Sequential on purpose: StockService shares this request's DbContext (not thread-safe).
        foreach (var (symbol, _) in holdings)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var history = await _stocks.GetPriceHistoryAsync(symbol, "1Y");
                result.Signals.Add(BuildSignals(symbol, history ?? new List<dotnetApp.Application.Dtos.PriceHistoryPointDto>()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Signals failed for {Symbol}", symbol);
                result.Signals.Add(new HoldingSignalsDto { Symbol = symbol });
            }
        }

        result.UpcomingDividends = await GetUpcomingDividendsAsync(holdings, ct);
        result.ExpectedDividendIncome = Math.Round(result.UpcomingDividends.Sum(d => d.ExpectedPayout), 2);
        return result;
    }

    /// <summary>Signals from daily closes (oldest first). Mirrors the web Technical Summary rules.</summary>
    internal static HoldingSignalsDto BuildSignals(string symbol, IReadOnlyList<dotnetApp.Application.Dtos.PriceHistoryPointDto> points)
    {
        var dto = new HoldingSignalsDto { Symbol = symbol, Sessions = points.Count };
        if (points.Count == 0) return dto;

        var sma50 = new Sma(50);
        var sma200 = new Sma(200);
        var rsi = new Rsi(14);
        foreach (var p in points)
        {
            sma50.Update(p.Close);
            sma200.Update(p.Close);
            rsi.Update(p.Close);
        }

        var last = points[^1].Close;
        dto.Sma50 = sma50.IsReady ? Math.Round(sma50.Value, 4) : null;
        dto.Sma200 = sma200.IsReady ? Math.Round(sma200.Value, 4) : null;

        if (sma50.IsReady)
        {
            var longRef = sma200.IsReady ? sma200.Value : sma50.Value;
            dto.Trend = last > sma50.Value && sma50.Value >= longRef ? "UPTREND"
                : last < sma50.Value && sma50.Value <= longRef ? "DOWNTREND"
                : "MIXED";
        }

        if (rsi.IsReady)
        {
            dto.Rsi14 = Math.Round(rsi.Value, 1);
            dto.RsiState = rsi.Value >= 70 ? "OVERBOUGHT" : rsi.Value <= 30 ? "OVERSOLD" : null;
        }

        var high = points.Max(p => p.High);
        var low = points.Where(p => p.Low > 0).Select(p => p.Low).DefaultIfEmpty(last).Min();
        dto.High52w = high;
        dto.Low52w = low;
        dto.RangePosition = high > low ? Math.Round((last - low) / (high - low) * 100, 1) : 50;
        dto.NearHigh52w = high > 0 && last >= high * (1 - NearExtremeThreshold);
        dto.NearLow52w = low > 0 && last <= low * (1 + NearExtremeThreshold);

        if (points.Count > 20)
        {
            var avg = points.Skip(points.Count - 21).Take(20).Average(p => p.Volume);
            if (avg > 0)
            {
                dto.VolumeRatio = Math.Round(points[^1].Volume / avg, 2);
                dto.VolumeSpike = dto.VolumeRatio >= VolumeSpikeRatio;
            }
        }

        return dto;
    }

    private async Task<List<HoldingDividendDto>> GetUpcomingDividendsAsync(
        List<(string Symbol, decimal Quantity)> holdings, CancellationToken ct)
    {
        var byBase = holdings
            .GroupBy(h => CseSymbols.BaseOf(h.Symbol))
            .ToDictionary(g => g.Key, g => g.ToList());
        if (byBase.Count == 0) return new List<HoldingDividendDto>();

        var bases = byBase.Keys.ToList();
        var today = ColomboTime.Today;

        var dividends = await _db.Dividends
            .AsNoTracking()
            .Where(d => bases.Contains(d.BaseSymbol) && d.RecordDate >= today)
            .OrderBy(d => d.RecordDate)
            .ToListAsync(ct);

        var result = new List<HoldingDividendDto>();
        foreach (var d in dividends)
        {
            foreach (var (symbol, qty) in byBase[d.BaseSymbol.ToUpperInvariant()])
            {
                // Non-voting shares (".X0000") are paid the non-voting rate.
                var dps = symbol.Contains(".X", StringComparison.OrdinalIgnoreCase) ? d.NonVotingDivPerShare : d.VotingDivPerShare;
                if (dps <= 0) continue;

                result.Add(new HoldingDividendDto
                {
                    Symbol = symbol,
                    CompanyName = d.CompanyName,
                    DividendPerShare = dps,
                    Quantity = qty,
                    ExpectedPayout = Math.Round(qty * dps, 2),
                    RecordDate = d.RecordDate,
                    PaymentDate = d.PaymentDate,
                    PaymentDateRaw = d.PaymentDateRaw,
                    FinancialYear = d.FinancialYear,
                });
            }
        }
        return result;
    }
}
