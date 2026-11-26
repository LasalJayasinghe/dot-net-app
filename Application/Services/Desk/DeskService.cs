using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.Dtos;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Services.Fundamentals;
using dotnetApp.Domain.Common;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace dotnetApp.Application.Services.Desk;

/// <summary>
/// The trading desk: the whole market (every CSE stock or tracked crypto pair), the user's positions
/// with their related data, watchlist, alerts and today's activity — assembled from caches and the
/// database so the page can refresh every 30 s without touching cse.lk.
/// </summary>
public class DeskService
{
    /// <summary>An active alert this close to its target (in % of price) shows under "Near alerts".</summary>
    private const decimal NearAlertPercent = 2m;

    private static readonly TimeSpan InsightsTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ValuationTtl = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly StockService _stocks;
    private readonly BinanceService _binance;
    private readonly MarketPriceResolver _prices;
    private readonly PortfolioInsightsService _insights;
    private readonly CseFundamentalsService _fundamentals;
    private readonly MarketIntradayStore _intraday;
    private readonly IMemoryCache _cache;

    public DeskService(
        AppDbContext db,
        StockService stocks,
        BinanceService binance,
        MarketPriceResolver prices,
        PortfolioInsightsService insights,
        CseFundamentalsService fundamentals,
        MarketIntradayStore intraday,
        IMemoryCache cache)
    {
        _db = db;
        _stocks = stocks;
        _binance = binance;
        _prices = prices;
        _insights = insights;
        _fundamentals = fundamentals;
        _intraday = intraday;
        _cache = cache;
    }

    public static bool IsCrypto(string? market) => string.Equals(market, "crypto", StringComparison.OrdinalIgnoreCase);

    // ── Overview ───────────────────────────────────────────────────────────────────────────────────

    public async Task<DeskOverviewDto> GetOverviewAsync(string userId, string? market, CancellationToken ct = default)
    {
        var crypto = IsCrypto(market);
        var dto = new DeskOverviewDto { Market = crypto ? "crypto" : "cse", Currency = crypto ? "USDT" : "LKR" };

        var (board, asOf) = crypto ? (CryptoBoard(), DateTime.UtcNow) : await CseBoardAsync(ct);
        dto.Board = board;
        dto.PricesAsOf = asOf;
        var bySymbol = board.ToDictionary(r => r.Symbol, StringComparer.OrdinalIgnoreCase);

        if (crypto)
        {
            dto.Session = new DeskSessionDto { IsOpen = true, IsTradingDay = true, AlwaysOpen = true };
            var btc = bySymbol.GetValueOrDefault("BTCUSDT");
            dto.Crypto = new DeskCryptoMarketDto
            {
                QuoteVolume24h = board.Sum(r => r.Turnover),
                Advancers = board.Count(r => r.ChangePercent > 0),
                Decliners = board.Count(r => r.ChangePercent < 0),
                BtcPrice = btc?.Price,
                BtcChangePercent = btc?.ChangePercent,
            };
        }
        else
        {
            dto.Session = await CseSessionAsync();
            dto.Cse = await CseMarketAsync(board, ct);
        }

        // Alerts first: positions and watchlist rows carry their counts.
        var alerts = await AlertsAsync(userId, crypto, ct);
        dto.AlertCounts = alerts.Where(a => a.IsActive)
            .GroupBy(a => a.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        dto.NearAlerts = alerts
            .Where(a => a.IsActive && a.DistancePercent is { } d && d >= 0 && d <= NearAlertPercent)
            .OrderBy(a => a.DistancePercent)
            .ToList();

        var portfolios = await PortfoliosAsync(userId, crypto, ct);
        dto.Portfolio = await PositionsAsync(userId, portfolios, bySymbol, crypto, dto.AlertCounts, ct);
        dto.PortfolioTotals = new DeskPortfolioTotalsDto
        {
            Currency = dto.Currency,
            HoldingsValue = Math.Round(dto.Portfolio.Sum(p => p.Value), 2),
            DayProfitLoss = Math.Round(dto.Portfolio.Sum(p => p.DayProfitLoss), 2),
            ProfitLoss = Math.Round(dto.Portfolio.Sum(p => p.ProfitLoss), 2),
            Cash = Math.Round(portfolios.Sum(p => p.CashBalance), 2),
        };

        var watchMarket = crypto ? WatchlistMarkets.Crypto : WatchlistMarkets.Cse;
        var watched = await _db.WatchlistItems.AsNoTracking()
            .Where(w => w.UserId == userId && w.Market == watchMarket)
            .OrderBy(w => w.CreatedAt)
            .Select(w => w.Symbol)
            .ToListAsync(ct);
        dto.Watchlist = watched
            .Select(s => bySymbol.GetValueOrDefault(Key(s, crypto)))
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();

        dto.Today = await TodayAsync(portfolios, alerts, ct);
        return dto;
    }

    // ── Selected symbol ────────────────────────────────────────────────────────────────────────────

    public async Task<DeskSymbolDto> GetSymbolAsync(string userId, string symbol, string? market, CancellationToken ct = default)
    {
        var crypto = IsCrypto(market);
        var key = Key(symbol, crypto);
        var dto = new DeskSymbolDto { Market = crypto ? "crypto" : "cse", Symbol = key, Currency = crypto ? "USDT" : "LKR" };

        var (board, _) = crypto ? (CryptoBoard(), DateTime.UtcNow) : await CseBoardAsync(ct);
        var bySymbol = board.ToDictionary(r => r.Symbol, StringComparer.OrdinalIgnoreCase);
        dto.Quote = bySymbol.GetValueOrDefault(key);

        var alerts = await AlertsAsync(userId, crypto, ct);
        dto.Alerts = alerts.Where(a => string.Equals(a.Symbol, key, StringComparison.OrdinalIgnoreCase)).ToList();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [key] = dto.Alerts.Count(a => a.IsActive) };

        var portfolios = await PortfoliosAsync(userId, crypto, ct);
        dto.Portfolios = portfolios.Select(p => new DeskPortfolioOptionDto
        {
            Id = p.Id,
            Name = p.Name,
            Cash = p.CashBalance,
            Quantity = p.Holdings.Where(h => Key(h.Symbol, crypto) == key).Sum(h => h.Quantity),
        }).ToList();

        var holding = portfolios
            .Select(p => new Portfolio
            {
                Id = p.Id, Name = p.Name, Type = p.Type, CashBalance = p.CashBalance,
                Holdings = p.Holdings.Where(h => Key(h.Symbol, crypto) == key).ToList(),
            })
            .Where(p => p.Holdings.Count > 0)
            .ToList();
        if (holding.Count > 0)
            dto.Position = (await PositionsAsync(userId, holding, bySymbol, crypto, counts, ct)).FirstOrDefault();

        if (!crypto) dto.Fundamentals = await _fundamentals.GetFundamentalsAsync(key, ct);
        return dto;
    }

    // ── Board ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every CSE security from the last tradeSummary poll. Before the first poll (just after a restart)
    /// falls back to the Stocks table, which lacks today's volume / turnover.
    /// </summary>
    private async Task<(List<DeskBoardRowDto> Rows, DateTime? AsOf)> CseBoardAsync(CancellationToken ct)
    {
        var snapshot = _stocks.GetCachedBoard();
        if (snapshot != null)
        {
            return (snapshot.Rows.Select(r => new DeskBoardRowDto
            {
                Symbol = r.Symbol,
                Name = r.Name,
                Price = r.Price,
                Change = r.Change,
                ChangePercent = Math.Round(r.PercentageChange, 2),
                Open = r.Open,
                High = r.High,
                Low = r.Low,
                PreviousClose = r.PreviousClose,
                Volume = r.ShareVolume,
                Trades = r.TradeVolume,
                Turnover = r.Turnover,
                MarketCap = r.MarketCap > 0 ? r.MarketCap : null,
            }).ToList(), snapshot.TakenAt);
        }

        var stored = await _db.Stocks.AsNoTracking()
            .Select(s => new { s.Symbol, s.Name, s.Price, s.ClosingPrice, s.Change, s.PercentageChange, s.High, s.Low, s.PreviousClose })
            .ToListAsync(ct);
        return (stored.Select(s => new DeskBoardRowDto
        {
            Symbol = s.Symbol,
            Name = s.Name ?? s.Symbol,
            Price = s.Price > 0 ? s.Price : s.ClosingPrice,
            Change = s.Change,
            ChangePercent = Math.Round(s.PercentageChange, 2),
            High = s.High,
            Low = s.Low,
            PreviousClose = s.PreviousClose,
        }).ToList(), null);
    }

    /// <summary>The tracked Binance pairs, live.</summary>
    private List<DeskBoardRowDto> CryptoBoard() =>
        BinanceService.TrackedSymbols
            .Select(s => _binance.GetTicker(s))
            .Where(t => t != null && t.LastPrice > 0)
            .Select(t => new DeskBoardRowDto
            {
                Symbol = t!.Symbol,
                Name = t.Symbol.EndsWith("USDT") ? t.Symbol[..^4] : t.Symbol,
                Price = t.LastPrice,
                Change = t.PriceChange,
                ChangePercent = Math.Round(t.PriceChangePercent, 2),
                Open = t.OpenPrice,
                High = t.High24h,
                Low = t.Low24h,
                PreviousClose = t.OpenPrice,
                Volume = t.Volume24h,
                Turnover = t.QuoteVolume24h,
            })
            .ToList();

    // ── CSE market panel ───────────────────────────────────────────────────────────────────────────

    private async Task<DeskSessionDto> CseSessionAsync()
    {
        var status = await _stocks.GetSavedMarketStatusAsync();
        return new DeskSessionDto
        {
            IsOpen = status?.IsOpen ?? false,
            IsTradingDay = status?.IsTradingDay ?? false,
            OpenTime = status?.OpenTime.ToString("HH:mm"),
            CloseTime = status?.CloseTime.ToString("HH:mm"),
        };
    }

    private async Task<DeskCseMarketDto> CseMarketAsync(List<DeskBoardRowDto> board, CancellationToken ct)
    {
        var aspi = await _stocks.GetSavedAspiDataAsync();
        var snp = await _stocks.GetSavedSnpDataAsync();
        var traded = board.Where(r => r.Trades is > 0).ToList();

        var dto = new DeskCseMarketDto
        {
            Aspi = Index(aspi),
            SpSl20 = Index(snp),
            AspiIntraday = _intraday.Today()
                .Select(p => new DeskIndexPointDto { At = p.At, Aspi = p.Aspi, SpSl20 = p.SpSl20 })
                .ToList(),
            Traded = traded.Count,
            Advancers = traded.Count(r => r.ChangePercent > 0),
            Decliners = traded.Count(r => r.ChangePercent < 0),
            Unchanged = traded.Count(r => r.ChangePercent == 0),
            Turnover = board.Sum(r => r.Turnover),
            Trades = board.Sum(r => r.Trades ?? 0),
            MarketCap = board.Sum(r => r.MarketCap ?? 0),
        };

        // Daily market summary: the latest published session, and a 20-session turnover average.
        var recent = await _db.CseMarketDaily.AsNoTracking()
            .OrderByDescending(m => m.TradeDate)
            .Take(20)
            .ToListAsync(ct);
        var latest = recent.FirstOrDefault();
        if (latest != null)
        {
            dto.DailyDate = latest.TradeDate;
            dto.ForeignPurchase = latest.EquityForeignPurchase;
            dto.ForeignSales = latest.EquityForeignSales;
            dto.MarketPer = latest.Per;
            dto.MarketPbv = latest.Pbv;
            dto.MarketDividendYield = latest.DividendYield;
        }
        var turnovers = recent.Where(m => m.MarketTurnover is > 0).Select(m => m.MarketTurnover!.Value).ToList();
        if (turnovers.Count > 0) dto.TurnoverAvg20d = Math.Round(turnovers.Average(), 0);

        return dto;
    }

    private static DeskIndexDto Index(StockIndicesDto i) => new()
    {
        Value = i.value,
        High = i.highValue,
        Low = i.lowValue,
        Change = i.change,
        ChangePercent = i.percentage,
    };

    // ── Positions ──────────────────────────────────────────────────────────────────────────────────

    private Task<List<Portfolio>> PortfoliosAsync(string userId, bool crypto, CancellationToken ct)
    {
        var type = crypto ? PortfolioType.Crypto : PortfolioType.Stocks;
        return _db.Portfolios.AsNoTracking()
            .Include(p => p.Holdings)
            .Where(p => p.UserId == userId && p.Type == type)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
    }

    /// <summary>Holdings summed per symbol across portfolios, priced from the board, with CSE extras.</summary>
    private async Task<List<DeskPositionDto>> PositionsAsync(
        string userId, List<Portfolio> portfolios, Dictionary<string, DeskBoardRowDto> board, bool crypto,
        IReadOnlyDictionary<string, int> alertCounts, CancellationToken ct)
    {
        var groups = portfolios
            .SelectMany(p => p.Holdings.Select(h => (Portfolio: p, Holding: h)))
            .GroupBy(x => Key(x.Holding.Symbol, crypto), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count == 0) return new List<DeskPositionDto>();

        var symbols = groups.Select(g => g.Key).ToList();
        var signals = crypto ? null : await CseInsightsAsync(userId, portfolios, ct);
        var valuation = crypto ? null : await ValuationInputsAsync(symbols, ct);

        var rows = new List<DeskPositionDto>();
        foreach (var g in groups)
        {
            var qty = g.Sum(x => x.Holding.Quantity);
            if (qty <= 0) continue;
            var rawCost = g.Sum(x => x.Holding.Quantity * x.Holding.AverageBuyPrice);
            var avg = rawCost / qty;

            board.TryGetValue(g.Key, out var quote);
            var price = quote?.Price ?? 0;
            var prev = quote?.PreviousClose is > 0 ? quote.PreviousClose : price;
            var value = qty * price;

            // Same fee model as the portfolio pages: CSE P/L after buy + sell charges; crypto as entered.
            decimal cost, pnl, breakEven;
            if (crypto)
            {
                cost = rawCost;
                pnl = value - cost;
                breakEven = avg / (1 - PortfolioService.DefaultCryptoFeeRate);
            }
            else
            {
                cost = CseFees.CostWithBuyFees(rawCost);
                pnl = CseFees.NetOfSale(value) - cost;
                breakEven = CseFees.BreakEvenPrice(avg);
            }

            var row = new DeskPositionDto
            {
                Symbol = g.Key,
                Name = quote?.Name ?? g.Key,
                Quantity = qty,
                AverageBuyPrice = Math.Round(avg, 4),
                Price = price,
                DayChangePercent = prev > 0 ? Math.Round((price - prev) / prev * 100, 2) : 0,
                DayProfitLoss = Math.Round(qty * (price - prev), 2),
                Value = Math.Round(value, 2),
                Cost = Math.Round(cost, 2),
                ProfitLoss = Math.Round(pnl, 2),
                ProfitLossPercent = cost > 0 ? Math.Round(pnl / cost * 100, 2) : 0,
                BreakEvenPrice = Math.Round(breakEven, 4),
                Volume = quote?.Volume ?? 0,
                Turnover = quote?.Turnover ?? 0,
                AlertCount = alertCounts.GetValueOrDefault(g.Key),
                Portfolios = g.Select(x => x.Portfolio.Name).Distinct().ToList(),
            };

            if (signals != null && signals.TryGetValue(g.Key, out var s))
            {
                row.Trend = s.Signals?.Trend;
                row.Rsi14 = s.Signals?.Rsi14;
                row.NextDividendRecordDate = s.Dividend?.RecordDate;
                row.NextDividendPerShare = s.Dividend?.DividendPerShare;
            }
            if (valuation != null && valuation.TryGetValue(g.Key, out var v) && price > 0)
            {
                row.PriceToEarnings = v.EpsTtm is > 0 ? Math.Round(price / v.EpsTtm.Value, 2) : null;
                row.PriceToBook = v.Nav is > 0 ? Math.Round(price / v.Nav.Value, 2) : null;
            }
            rows.Add(row);
        }

        var total = rows.Sum(r => r.Value);
        foreach (var r in rows) r.WeightPercent = total > 0 ? Math.Round(r.Value / total * 100, 2) : 0;
        return rows.OrderByDescending(r => r.Value).ToList();
    }

    private sealed record SymbolInsights(HoldingSignalsDto? Signals, HoldingDividendDto? Dividend);

    /// <summary>Trend / RSI and the next dividend per held CSE stock (cached 10 min per user — it reads a year of prices).</summary>
    private async Task<Dictionary<string, SymbolInsights>> CseInsightsAsync(
        string userId, List<Portfolio> portfolios, CancellationToken ct)
    {
        var cacheKey = $"desk-insights:{userId}:{string.Join(',', portfolios.Select(p => p.Id))}";
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, SymbolInsights>? cached) && cached != null) return cached;

        var signals = new Dictionary<string, HoldingSignalsDto>(StringComparer.OrdinalIgnoreCase);
        var dividends = new Dictionary<string, HoldingDividendDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in portfolios.Where(p => p.Holdings.Count > 0))
        {
            var insights = await _insights.GetInsightsAsync(userId, p.Id, ct);
            if (insights == null) continue;
            foreach (var s in insights.Signals) signals[CseSymbols.ToLookup(s.Symbol)] = s;
            foreach (var d in insights.UpcomingDividends)
            {
                var k = CseSymbols.ToLookup(d.Symbol);
                if (!dividends.TryGetValue(k, out var existing) || d.RecordDate < existing.RecordDate) dividends[k] = d;
            }
        }

        var result = signals.Keys.Union(dividends.Keys, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k, k => new SymbolInsights(signals.GetValueOrDefault(k), dividends.GetValueOrDefault(k)),
                StringComparer.OrdinalIgnoreCase);
        _cache.Set(cacheKey, result, InsightsTtl);
        return result;
    }

    private async Task<Dictionary<string, (decimal? Nav, decimal? EpsTtm)>> ValuationInputsAsync(
        List<string> symbols, CancellationToken ct)
    {
        var cacheKey = "desk-valuation:" + string.Join(',', symbols.OrderBy(s => s));
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, (decimal? Nav, decimal? EpsTtm)>? cached) && cached != null)
            return cached;
        var inputs = await _fundamentals.GetValuationInputsAsync(symbols, ct);
        _cache.Set(cacheKey, inputs, ValuationTtl);
        return inputs;
    }

    // ── Alerts & today ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The user's alerts for this market (CSE: stocks and indices; crypto: pairs and USDT/LKR) that are
    /// active or fired today, with current price and distance to the target.
    /// </summary>
    private async Task<List<DeskAlertDto>> AlertsAsync(string userId, bool crypto, CancellationToken ct)
    {
        var startUtc = TodayStartUtc();
        var alerts = (await _db.Alerts.AsNoTracking()
                .Where(a => a.CreatedBy == userId && (a.IsActive || a.LastTriggeredAt >= startUtc))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(ct))
            .Where(a => (MarketPriceResolver.AssetTypeOf(a.Symbol) is AlertAssetType.Crypto or AlertAssetType.Fx) == crypto)
            .ToList();
        if (alerts.Count == 0) return new List<DeskAlertDto>();

        var prices = await _prices.GetPricesAsync(alerts.Select(a => a.Symbol), ct);
        return alerts.Select(a =>
        {
            decimal? price = prices.TryGetValue(a.Symbol, out var p) ? p : null;
            // Positive = the move still needed to trigger; negative = already past the target.
            decimal? distance = price is > 0
                ? Math.Round((a.IsAbove ? a.TargetPrice - price.Value : price.Value - a.TargetPrice) / price.Value * 100, 2)
                : null;
            return new DeskAlertDto
            {
                Id = a.Id,
                Symbol = a.Symbol,
                Label = MarketPriceResolver.LabelOf(a.Symbol),
                TargetPrice = a.TargetPrice,
                IsAbove = a.IsAbove,
                IsActive = a.IsActive,
                FireOnce = a.FireOnce,
                CurrentPrice = price,
                DistancePercent = distance,
                LastTriggeredAt = a.LastTriggeredAt,
            };
        }).ToList();
    }

    private async Task<DeskTodayDto> TodayAsync(List<Portfolio> portfolios, List<DeskAlertDto> alerts, CancellationToken ct)
    {
        var startUtc = TodayStartUtc();
        var ids = portfolios.Select(p => p.Id).ToList();
        var names = portfolios.ToDictionary(p => p.Id, p => p.Name);

        var trades = await _db.PortfolioTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.PortfolioId) && t.ExecutedAt >= startUtc &&
                        (t.Type == PortfolioTransactionType.Buy || t.Type == PortfolioTransactionType.Sell))
            .OrderByDescending(t => t.ExecutedAt)
            .ToListAsync(ct);

        return new DeskTodayDto
        {
            Trades = trades.Select(t => new DeskTradeDto
            {
                At = t.ExecutedAt,
                Portfolio = names.GetValueOrDefault(t.PortfolioId, ""),
                Side = t.Type.ToString(),
                Symbol = t.Symbol ?? "",
                Quantity = t.Quantity ?? 0,
                Price = t.Price ?? 0,
                RealizedProfitLoss = t.RealizedProfitLoss,
            }).ToList(),
            RealizedProfitLoss = Math.Round(trades.Sum(t => t.RealizedProfitLoss ?? 0), 2),
            AlertsFired = alerts
                .Where(a => a.LastTriggeredAt >= startUtc)
                .OrderByDescending(a => a.LastTriggeredAt)
                .ToList(),
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Board key: CSE codes in full ("COMB" → "COMB.N0000"), crypto pairs upper-case.</summary>
    private static string Key(string symbol, bool crypto) =>
        crypto ? symbol.Trim().ToUpperInvariant() : CseSymbols.ToLookup(symbol);

    /// <summary>Midnight in Colombo, as UTC — "today" for trades and fired alerts.</summary>
    private static DateTime TodayStartUtc() => DateTime.SpecifyKind(ColomboTime.Today - ColomboTime.Offset, DateTimeKind.Utc);
}
