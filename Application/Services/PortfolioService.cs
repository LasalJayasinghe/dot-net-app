using dotnetApp.Application.Dtos;
using dotnetApp.Domain.Common;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using dotnetApp.Controllers.Api;

namespace dotnetApp.Application.Services;

/// <summary>
/// Manages user portfolios (Stocks / Crypto) and calculates live valuations
/// and cross-currency Net Worth aggregation (USDT → LKR).
///
/// Prices: CSE stocks come from the Stocks table (refreshed by AlertJob from CSE's tradeSummary),
/// crypto from the Binance WebSocket feed. All holdings of a request are priced with ONE stocks query.
/// Stock P/L is net of CSE selling charges (<see cref="CseFees.SellRate"/>).
/// </summary>
public class PortfolioService
{
    private readonly AppDbContext  _db;
    private readonly BinanceService _binance;
    private readonly StockService _stocks;
    private readonly ILogger<PortfolioService> _logger;

    public PortfolioService(AppDbContext db, BinanceService binance, StockService stocks, ILogger<PortfolioService> logger)
    {
        _db      = db;
        _binance = binance;
        _stocks  = stocks;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Portfolio CRUD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PortfolioDetailDto> CreatePortfolioAsync(string userId, CreatePortfolioRequest req)
    {
        var portfolio = new Portfolio
        {
            UserId       = userId,
            Name         = req.Name,
            Type         = req.Type,
            BaseCurrency = req.BaseCurrency.ToUpperInvariant(),
            Description  = req.Description
        };

        _db.Portfolios.Add(portfolio);
        await _db.SaveChangesAsync();

        return ToDetailDto(portfolio, new List<HoldingDto>(), Valuation.Empty);
    }

    public async Task<List<PortfolioSummaryDto>> GetUserPortfoliosAsync(string userId, PortfolioType? filter = null)
    {
        var query = _db.Portfolios
            .Include(p => p.Holdings)
            .Where(p => p.UserId == userId);

        if (filter.HasValue)
            query = query.Where(p => p.Type == filter.Value);

        var portfolios = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        var quotes = await LoadQuotesAsync(portfolios);

        return portfolios.Select(p => ToSummaryDto(p, ValuePortfolio(p, quotes))).ToList();
    }

    public async Task<PortfolioDetailDto?> GetPortfolioDetailAsync(string userId, int portfolioId)
    {
        var portfolio = await _db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

        if (portfolio == null) return null;

        var quotes = await LoadQuotesAsync(new[] { portfolio });
        var valuation = ValuePortfolio(portfolio, quotes);
        var activity = _stocks.GetCachedActivity();

        var holdingDtos = portfolio.Holdings
            .Select(h => BuildHoldingDto(h, portfolio.Type, quotes, valuation.Value, activity))
            .ToList();

        return ToDetailDto(portfolio, holdingDtos, valuation);
    }

    /// <summary>Cheap ownership check — no pricing, no holdings load.</summary>
    public Task<bool> OwnsPortfolioAsync(string userId, int portfolioId)
        => _db.Portfolios.AsNoTracking().AnyAsync(p => p.Id == portfolioId && p.UserId == userId);

    public async Task<bool> UpdatePortfolioAsync(string userId, int portfolioId, UpdatePortfolioRequest req)
    {
        var portfolio = await _db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
        if (portfolio == null) return false;

        if (!string.IsNullOrWhiteSpace(req.Name))        portfolio.Name        = req.Name;
        if (req.Description != null)                     portfolio.Description = req.Description;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeletePortfolioAsync(string userId, int portfolioId)
    {
        var portfolio = await _db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
        if (portfolio == null) return false;

        _db.Portfolios.Remove(portfolio);
        await _db.SaveChangesAsync();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Holdings CRUD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(bool success, string message, HoldingDto? holding)> AddOrUpdateHoldingAsync(
        string userId, int portfolioId, AddHoldingRequest req)
    {
        var portfolio = await _db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

        if (portfolio == null) return (false, "Portfolio not found.", null);

        var symbol    = req.Symbol.ToUpperInvariant();
        var assetType = portfolio.Type == PortfolioType.Stocks ? AssetType.Stock : AssetType.Crypto;

        var existing = portfolio.Holdings.FirstOrDefault(h => h.Symbol == symbol);
        if (existing != null)
        {
            // Update via weighted average for re-buys
            decimal totalQty     = existing.Quantity + req.Quantity;
            decimal weightedAvg  = ((existing.Quantity * existing.AverageBuyPrice) + (req.Quantity * req.AverageBuyPrice)) / totalQty;
            existing.Quantity        = totalQty;
            existing.AverageBuyPrice = weightedAvg;
            existing.UpdatedAt       = DateTime.UtcNow;
            if (req.Notes != null) existing.Notes = req.Notes;
        }
        else
        {
            portfolio.Holdings.Add(new PortfolioHolding
            {
                PortfolioId      = portfolioId,
                Symbol           = symbol,
                AssetType        = assetType,
                Quantity         = req.Quantity,
                AverageBuyPrice  = req.AverageBuyPrice,
                Notes            = req.Notes
            });
        }

        await _db.SaveChangesAsync();

        // Return the holding DTO with current price (weight relative to the updated portfolio)
        var quotes = await LoadQuotesAsync(new[] { portfolio });
        var valuation = ValuePortfolio(portfolio, quotes);
        var h = existing ?? portfolio.Holdings.Last();
        var dto = BuildHoldingDto(h, portfolio.Type, quotes, valuation.Value, _stocks.GetCachedActivity());
        return (true, "Holding saved.", dto);
    }

    public async Task<bool> DeleteHoldingAsync(string userId, int portfolioId, int holdingId)
    {
        var holding = await _db.PortfolioHoldings
            .Include(h => h.Portfolio)
            .FirstOrDefaultAsync(h => h.Id == holdingId && h.PortfolioId == portfolioId && h.Portfolio.UserId == userId);

        if (holding == null) return false;

        _db.PortfolioHoldings.Remove(holding);
        await _db.SaveChangesAsync();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Net Worth (cross-currency aggregation)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<NetWorthOverviewDto> GetNetWorthAsync(string userId)
    {
        var portfolios = await _db.Portfolios
            .Include(p => p.Holdings)
            .Where(p => p.UserId == userId)
            .ToListAsync();

        // Get the exchange rates from DB
        decimal usdtToLkr = await GetExchangeRateAsync("USDT", "LKR");
        decimal lkrToUsdt = await GetExchangeRateAsync("LKR", "USDT");

        if (lkrToUsdt == 0 && usdtToLkr > 0) lkrToUsdt = 1m / usdtToLkr; // fallback

        var quotes = await LoadQuotesAsync(portfolios);

        decimal totalNetWorthLkr = 0;
        decimal totalNetWorthUsdt = 0;
        decimal totalPnlLkr      = 0;
        decimal totalPnlUsdt     = 0;
        var summaries = new List<PortfolioSummaryDto>();

        foreach (var p in portfolios)
        {
            var v = ValuePortfolio(p, quotes);

            // Convert to display currency
            decimal rateToLkr = GetConversionRate(p.BaseCurrency, "LKR", usdtToLkr, lkrToUsdt);
            decimal rateToUsdt = GetConversionRate(p.BaseCurrency, "USDT", usdtToLkr, lkrToUsdt);

            totalNetWorthLkr  += v.Value * rateToLkr;
            totalNetWorthUsdt += v.Value * rateToUsdt;
            totalPnlLkr       += v.ProfitLoss * rateToLkr;
            totalPnlUsdt      += v.ProfitLoss * rateToUsdt;

            summaries.Add(ToSummaryDto(p, v));
        }

        return new NetWorthOverviewDto
        {
            TotalNetWorthLkr = totalNetWorthLkr,
            TotalNetWorthUsdt = totalNetWorthUsdt,
            TotalProfitLossLkr = totalPnlLkr,
            TotalProfitLossUsdt = totalPnlUsdt,
            UsdtToLkrRate = usdtToLkr,
            LkrToUsdtRate = lkrToUsdt,
            Portfolios = summaries
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    public async Task SyncHoldingsAsync(int portfolioId, string userId, List<ParsedHoldingDto> parsedHoldings)
    {
        var portfolio = await _db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

        if (portfolio == null) return;

        // Remove existing holdings
        _db.PortfolioHoldings.RemoveRange(portfolio.Holdings);

        // Add new holdings from PDF
        foreach (var h in parsedHoldings)
        {
            portfolio.Holdings.Add(new PortfolioHolding
            {
                PortfolioId = portfolioId,
                Symbol = h.Symbol.ToUpperInvariant(),
                AssetType = portfolio.Type == PortfolioType.Stocks ? AssetType.Stock : AssetType.Crypto,
                Quantity = h.Quantity,
                AverageBuyPrice = h.AverageBuyPrice
            });
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>Current and previous-session price for one holding.</summary>
    private sealed record Quote(decimal Price, decimal PreviousClose, string? Name);

    /// <summary>Portfolio totals. ProfitLoss is net of CSE selling charges for stock portfolios.</summary>
    private sealed record Valuation(
        decimal Value, decimal Cost, decimal ProfitLoss, decimal ProfitLossPercent,
        decimal TodayProfitLoss, decimal TodayProfitLossPercent)
    {
        public static readonly Valuation Empty = new(0, 0, 0, 0, 0, 0);
    }

    /// <summary>Key into the quote map: CSE symbols are normalised ("COMB" → "COMB.N0000").</summary>
    private static string QuoteKey(PortfolioType type, string symbol) =>
        type == PortfolioType.Crypto ? "C:" + symbol.ToUpperInvariant() : "S:" + CseSymbols.ToLookup(symbol);

    /// <summary>
    /// Prices every holding of the given portfolios: one Stocks query for all CSE symbols,
    /// Binance's in-memory 24h tickers for crypto (previous close = 24h open).
    /// </summary>
    private async Task<Dictionary<string, Quote>> LoadQuotesAsync(IEnumerable<Portfolio> portfolios)
    {
        var quotes = new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);
        var cseSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in portfolios)
        {
            foreach (var h in p.Holdings)
            {
                if (p.Type == PortfolioType.Stocks)
                {
                    cseSymbols.Add(CseSymbols.ToLookup(h.Symbol));
                    continue;
                }

                var key = QuoteKey(p.Type, h.Symbol);
                if (quotes.ContainsKey(key)) continue;
                var ticker = _binance.GetTicker(h.Symbol);
                var price = ticker?.LastPrice ?? _binance.Prices.GetValueOrDefault(h.Symbol.ToUpperInvariant(), 0);
                quotes[key] = new Quote(price, ticker?.OpenPrice ?? 0, null);
            }
        }

        if (cseSymbols.Count > 0)
        {
            var lookups = cseSymbols.ToList();
            var rows = await _db.Stocks.AsNoTracking()
                .Where(s => lookups.Contains(s.Symbol))
                .Select(s => new { s.Symbol, s.Name, s.Price, s.ClosingPrice, s.PreviousClose })
                .ToListAsync();

            foreach (var r in rows)
                quotes["S:" + r.Symbol.ToUpperInvariant()] =
                    new Quote(r.Price > 0 ? r.Price : r.ClosingPrice, r.PreviousClose, r.Name);
        }

        return quotes;
    }

    private static (decimal Price, decimal PreviousClose) PriceOf(
        PortfolioHolding h, PortfolioType type, Dictionary<string, Quote> quotes)
    {
        if (!quotes.TryGetValue(QuoteKey(type, h.Symbol), out var q)) return (0, 0);
        // No previous close (e.g. first listing day): treat today's change as zero.
        return (q.Price, q.PreviousClose > 0 ? q.PreviousClose : q.Price);
    }

    /// <summary>
    /// Cost basis of a holding. CSE average buy prices are raw trade prices (as in ATrad), so the
    /// 1.12% buy charges are added; crypto uses the price as entered.
    /// </summary>
    private static decimal CostOf(PortfolioHolding h, bool isStock) =>
        isStock ? CseFees.CostWithBuyFees(h.Quantity * h.AverageBuyPrice) : h.Quantity * h.AverageBuyPrice;

    private static Valuation ValuePortfolio(Portfolio p, Dictionary<string, Quote> quotes)
    {
        var isStock = p.Type == PortfolioType.Stocks;
        decimal value = 0, cost = 0, previousValue = 0;
        foreach (var h in p.Holdings)
        {
            var (price, prevClose) = PriceOf(h, p.Type, quotes);
            value         += h.Quantity * price;
            cost          += CostOf(h, isStock);
            previousValue += h.Quantity * prevClose;
        }

        // Stocks: P/L after both buy and sell charges, so P/L is exactly 0 at the break-even price.
        var pnl = isStock ? CseFees.NetOfSale(value) - cost : value - cost;
        var today = value - previousValue;

        return new Valuation(
            value, cost, pnl,
            cost > 0 ? pnl / cost * 100 : 0,
            today,
            previousValue > 0 ? today / previousValue * 100 : 0);
    }

    private static HoldingDto BuildHoldingDto(
        PortfolioHolding h, PortfolioType type, Dictionary<string, Quote> quotes, decimal portfolioValue,
        IReadOnlyDictionary<string, StockActivityDto>? activity)
    {
        var isStock = type == PortfolioType.Stocks;
        quotes.TryGetValue(QuoteKey(type, h.Symbol), out var quote);
        var (currentPrice, prevClose) = PriceOf(h, type, quotes);

        decimal currentValue = h.Quantity * currentPrice;
        decimal cost         = CostOf(h, isStock);
        decimal pnl          = isStock ? CseFees.NetOfSale(currentValue) - cost : currentValue - cost;
        decimal pnlPct       = cost > 0 ? (pnl / cost) * 100 : 0;
        decimal dayChange    = currentPrice - prevClose;

        StockActivityDto? act = null;
        if (isStock) activity?.TryGetValue(CseSymbols.ToLookup(h.Symbol), out act);

        return new HoldingDto
        {
            Id                  = h.Id,
            Symbol              = h.Symbol,
            Name                = quote?.Name,
            AssetType           = h.AssetType.ToString(),
            Quantity            = h.Quantity,
            AverageBuyPrice     = h.AverageBuyPrice,
            CostPerShare        = isStock ? Math.Round(CseFees.CostPerShare(h.AverageBuyPrice), 4) : null,
            BreakEvenPrice      = isStock ? Math.Round(CseFees.BreakEvenPrice(h.AverageBuyPrice), 4) : null,
            CurrentPrice        = currentPrice,
            PreviousClose       = prevClose,
            DayChange           = Math.Round(dayChange, 4),
            DayChangePercent    = prevClose > 0 ? Math.Round(dayChange / prevClose * 100, 2) : 0,
            DayProfitLoss       = Math.Round(h.Quantity * dayChange, 4),
            CurrentValue        = Math.Round(currentValue, 4),
            WeightPercent       = portfolioValue > 0 ? Math.Round(currentValue / portfolioValue * 100, 2) : 0,
            ProfitLoss          = Math.Round(pnl, 4),
            ProfitLossPercent   = Math.Round(pnlPct, 2),
            ShareVolume         = act?.ShareVolume,
            TradeVolume         = act?.TradeVolume,
            Turnover            = act?.Turnover,
            Notes               = h.Notes
        };
    }

    private async Task<decimal> GetExchangeRateAsync(string from, string to)
    {
        var rate = await _db.CurrencyExchangeRates
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FromCurrency == from && r.ToCurrency == to);

        // Fallback: 1:1 if no rate seeded yet (same currency or rate not loaded)
        return rate?.Rate ?? (from == to ? 1m : 0m);
    }

    private static decimal GetConversionRate(string fromCurrency, string toCurrency, decimal usdtToLkr, decimal lkrToUsdt)
    {
        if (fromCurrency == toCurrency) return 1m;
        if (fromCurrency == "USDT" && toCurrency == "LKR") return usdtToLkr;
        if (fromCurrency == "LKR"  && toCurrency == "USDT") return lkrToUsdt;
        return 1m; // Default: no conversion if pair not handled
    }

    private static PortfolioSummaryDto ToSummaryDto(Portfolio p, Valuation v) => new()
    {
        Id                     = p.Id,
        Name                   = p.Name,
        Type                   = p.Type.ToString(),
        BaseCurrency           = p.BaseCurrency,
        Description            = p.Description,
        TotalValue             = Math.Round(v.Value, 4),
        TotalProfitLoss        = Math.Round(v.ProfitLoss, 4),
        TotalProfitLossPercent = Math.Round(v.ProfitLossPercent, 2),
        TodayProfitLoss        = Math.Round(v.TodayProfitLoss, 4),
        TodayProfitLossPercent = Math.Round(v.TodayProfitLossPercent, 2),
        HoldingCount           = p.Holdings.Count,
        CreatedAt              = p.CreatedAt
    };

    private static PortfolioDetailDto ToDetailDto(Portfolio portfolio, List<HoldingDto> holdings, Valuation v)
        => new()
        {
            Id                     = portfolio.Id,
            Name                   = portfolio.Name,
            Type                   = portfolio.Type.ToString(),
            BaseCurrency           = portfolio.BaseCurrency,
            Description            = portfolio.Description,
            TotalValue             = Math.Round(v.Value, 4),
            TotalCost              = Math.Round(v.Cost, 4),
            TotalProfitLoss        = Math.Round(v.ProfitLoss, 4),
            TotalProfitLossPercent = Math.Round(v.ProfitLossPercent, 2),
            TodayProfitLoss        = Math.Round(v.TodayProfitLoss, 4),
            TodayProfitLossPercent = Math.Round(v.TodayProfitLossPercent, 2),
            CreatedAt              = portfolio.CreatedAt,
            Holdings               = holdings
        };
}
