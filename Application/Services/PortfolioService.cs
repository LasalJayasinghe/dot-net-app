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

        return ToDetailDto(portfolio, new List<HoldingDto>(), Valuation.Empty, realizedProfitLoss: 0);
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

        var realized = await _db.PortfolioTransactions.AsNoTracking()
            .Where(t => t.PortfolioId == portfolioId && t.RealizedProfitLoss != null)
            .SumAsync(t => t.RealizedProfitLoss) ?? 0m;

        return ToDetailDto(portfolio, holdingDtos, valuation, realized);
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

        // A position added without paying from cash (e.g. one you already held).
        Record(portfolio, PortfolioTransactionType.Buy, symbol, req.Quantity, req.AverageBuyPrice,
            fees: 0, cashChange: 0, notes: req.Notes ?? "Added without using cash");

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

        Record(holding.Portfolio, PortfolioTransactionType.HoldingAdjustment, holding.Symbol, -holding.Quantity,
            holding.AverageBuyPrice, fees: 0, cashChange: 0,
            notes: $"Removed holding ({holding.Quantity:0.########} @ {holding.AverageBuyPrice:0.####})");
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
        decimal totalCashLkr     = 0;
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
            totalCashLkr      += v.Cash * rateToLkr;

            summaries.Add(ToSummaryDto(p, v));
        }

        return new NetWorthOverviewDto
        {
            TotalNetWorthLkr = totalNetWorthLkr,
            TotalNetWorthUsdt = totalNetWorthUsdt,
            TotalProfitLossLkr = totalPnlLkr,
            TotalProfitLossUsdt = totalPnlUsdt,
            TotalCashLkr = totalCashLkr,
            UsdtToLkrRate = usdtToLkr,
            LkrToUsdtRate = lkrToUsdt,
            Portfolios = summaries
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Trades, cash and the ledger
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Binance spot fee used when a crypto trade doesn't state its own.</summary>
    public const decimal DefaultCryptoFeeRate = 0.001m;

    /// <summary>
    /// Buy or sell. CSE trades use the standard 1.12% charges (the same ones the P/L uses); crypto uses
    /// the given fee or 0.1%. Buys raise the holding at a weighted average and (by default) take the cost
    /// from cash; sells reduce it, add the net proceeds to cash and record realised P/L.
    /// </summary>
    public async Task<(bool Success, string Message, PortfolioDetailDto? Detail)> TradeAsync(
        string userId, int portfolioId, TradeRequest req)
    {
        var portfolio = await _db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
        if (portfolio == null) return (false, "Portfolio not found.", null);

        var isStock = portfolio.Type == PortfolioType.Stocks;
        var symbol = req.Symbol.Trim().ToUpperInvariant();
        var gross = req.Quantity * req.Price;
        var holding = portfolio.Holdings.FirstOrDefault(h => h.Symbol == symbol);
        var ccy = portfolio.BaseCurrency;

        if (string.Equals(req.Side, "Buy", StringComparison.OrdinalIgnoreCase))
        {
            // Stocks keep the raw trade price as the average (fees are added in the valuation, as in
            // ATrad); crypto folds the fee into the average so its P/L is after fees too.
            decimal fees, cashCost, costAdded;
            if (isStock)
            {
                cashCost = CseFees.CostWithBuyFees(gross);
                fees = cashCost - gross;
                costAdded = gross;
            }
            else
            {
                fees = req.Fees ?? Math.Round(gross * DefaultCryptoFeeRate, 8);
                cashCost = gross + fees;
                costAdded = cashCost;
            }

            if (req.UseCash && portfolio.CashBalance < cashCost)
                return (false,
                    $"Not enough cash: {ccy} {portfolio.CashBalance:#,0.##} available, this buy needs {ccy} {cashCost:#,0.##}. " +
                    "Deposit first, or untick \"Pay from cash\" to record a position you already had.", null);

            if (holding == null)
            {
                holding = new PortfolioHolding
                {
                    PortfolioId = portfolioId,
                    Symbol = symbol,
                    AssetType = isStock ? AssetType.Stock : AssetType.Crypto,
                    Quantity = req.Quantity,
                    AverageBuyPrice = costAdded / req.Quantity,
                    Notes = req.Notes,
                };
                portfolio.Holdings.Add(holding);
            }
            else
            {
                var qty = holding.Quantity + req.Quantity;
                holding.AverageBuyPrice = (holding.Quantity * holding.AverageBuyPrice + costAdded) / qty;
                holding.Quantity = qty;
                holding.UpdatedAt = DateTime.UtcNow;
            }

            var cashChange = req.UseCash ? -cashCost : 0;
            portfolio.CashBalance += cashChange;
            Record(portfolio, PortfolioTransactionType.Buy, symbol, req.Quantity, req.Price, fees, cashChange,
                req.Notes ?? (req.UseCash ? null : "Added without using cash"));
        }
        else if (string.Equals(req.Side, "Sell", StringComparison.OrdinalIgnoreCase))
        {
            if (holding == null) return (false, $"You don't hold {symbol} in this portfolio.", null);
            if (req.Quantity > holding.Quantity)
                return (false, $"You hold {holding.Quantity:0.########} {symbol}; you can't sell {req.Quantity:0.########}.", null);

            decimal fees, proceeds, costBasis;
            if (isStock)
            {
                proceeds = CseFees.NetOfSale(gross);
                fees = gross - proceeds;
                costBasis = CseFees.CostWithBuyFees(req.Quantity * holding.AverageBuyPrice);
            }
            else
            {
                fees = req.Fees ?? Math.Round(gross * DefaultCryptoFeeRate, 8);
                proceeds = gross - fees;
                costBasis = req.Quantity * holding.AverageBuyPrice;
            }

            holding.Quantity -= req.Quantity;
            holding.UpdatedAt = DateTime.UtcNow;
            // Sold out (allowing for dust from fractional crypto amounts): drop the holding.
            if (holding.Quantity <= 0.00000001m) _db.PortfolioHoldings.Remove(holding);

            var cashChange = req.UseCash ? proceeds : 0;
            portfolio.CashBalance += cashChange;
            Record(portfolio, PortfolioTransactionType.Sell, symbol, req.Quantity, req.Price, fees, cashChange,
                req.Notes, realized: proceeds - costBasis);
        }
        else
        {
            return (false, "Side must be Buy or Sell.", null);
        }

        await _db.SaveChangesAsync();
        return (true, "Trade recorded.", await GetPortfolioDetailAsync(userId, portfolioId));
    }

    /// <summary>Deposit, withdraw, or set the cash balance to match the broker / exchange.</summary>
    public async Task<(bool Success, string Message, PortfolioDetailDto? Detail)> ChangeCashAsync(
        string userId, int portfolioId, CashRequest req)
    {
        var portfolio = await _db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);
        if (portfolio == null) return (false, "Portfolio not found.", null);

        var ccy = portfolio.BaseCurrency;
        PortfolioTransactionType type;
        decimal change;
        switch (req.Action.Trim().ToLowerInvariant())
        {
            case "deposit":
                if (req.Amount <= 0) return (false, "Deposit amount must be greater than zero.", null);
                type = PortfolioTransactionType.Deposit;
                change = req.Amount;
                break;
            case "withdraw":
                if (req.Amount <= 0) return (false, "Withdrawal amount must be greater than zero.", null);
                if (req.Amount > portfolio.CashBalance)
                    return (false, $"Only {ccy} {portfolio.CashBalance:#,0.##} is available to withdraw.", null);
                type = PortfolioTransactionType.Withdrawal;
                change = -req.Amount;
                break;
            case "set":
                if (req.Amount < 0) return (false, "Cash balance can't be negative.", null);
                type = PortfolioTransactionType.CashAdjustment;
                change = req.Amount - portfolio.CashBalance;
                if (change == 0) return (true, "Cash balance unchanged.", await GetPortfolioDetailAsync(userId, portfolioId));
                break;
            default:
                return (false, "Action must be Deposit, Withdraw or Set.", null);
        }

        portfolio.CashBalance += change;
        Record(portfolio, type, null, null, null, fees: 0, cashChange: change, req.Notes);
        await _db.SaveChangesAsync();
        return (true, "Cash updated.", await GetPortfolioDetailAsync(userId, portfolioId));
    }

    /// <summary>Corrects a holding's quantity / average price (no cash moves); the old values are kept in the ledger.</summary>
    public async Task<(bool Success, string Message, PortfolioDetailDto? Detail)> UpdateHoldingAsync(
        string userId, int portfolioId, int holdingId, UpdateHoldingRequest req)
    {
        var holding = await _db.PortfolioHoldings
            .Include(h => h.Portfolio)
            .FirstOrDefaultAsync(h => h.Id == holdingId && h.PortfolioId == portfolioId && h.Portfolio.UserId == userId);
        if (holding == null) return (false, "Holding not found.", null);

        var was = $"{holding.Quantity:0.########} @ {holding.AverageBuyPrice:0.####}";
        Record(holding.Portfolio, PortfolioTransactionType.HoldingAdjustment, holding.Symbol,
            req.Quantity - holding.Quantity, req.AverageBuyPrice, fees: 0, cashChange: 0,
            req.Notes ?? $"Corrected from {was}");

        holding.Quantity = req.Quantity;
        holding.AverageBuyPrice = req.AverageBuyPrice;
        holding.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, "Holding updated.", await GetPortfolioDetailAsync(userId, portfolioId));
    }

    /// <summary>Newest first; null when the portfolio isn't the user's.</summary>
    public async Task<List<PortfolioTransactionDto>?> GetTransactionsAsync(string userId, int portfolioId, int limit)
    {
        if (!await OwnsPortfolioAsync(userId, portfolioId)) return null;
        return await _db.PortfolioTransactions.AsNoTracking()
            .Where(t => t.PortfolioId == portfolioId)
            .OrderByDescending(t => t.ExecutedAt).ThenByDescending(t => t.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(t => new PortfolioTransactionDto
            {
                Id = t.Id,
                Type = t.Type.ToString(),
                Symbol = t.Symbol,
                Quantity = t.Quantity,
                Price = t.Price,
                Fees = t.Fees,
                CashChange = t.CashChange,
                CashBalanceAfter = t.CashBalanceAfter,
                RealizedProfitLoss = t.RealizedProfitLoss,
                Notes = t.Notes,
                ExecutedAt = t.ExecutedAt,
            })
            .ToListAsync();
    }

    /// <summary>Adds a ledger entry. Call after changing CashBalance so CashBalanceAfter is right.</summary>
    private void Record(
        Portfolio portfolio, PortfolioTransactionType type, string? symbol, decimal? quantity, decimal? price,
        decimal fees, decimal cashChange, string? notes, decimal? realized = null)
    {
        _db.PortfolioTransactions.Add(new PortfolioTransaction
        {
            PortfolioId = portfolio.Id,
            Type = type,
            Symbol = symbol,
            Quantity = quantity,
            Price = price,
            Fees = Math.Round(fees, 8),
            CashChange = Math.Round(cashChange, 8),
            CashBalanceAfter = Math.Round(portfolio.CashBalance, 8),
            RealizedProfitLoss = realized is { } r ? Math.Round(r, 8) : null,
            Notes = notes is { Length: > 255 } ? notes[..255] : notes,
        });
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
        Record(portfolio, PortfolioTransactionType.HoldingAdjustment, null, null, null, fees: 0, cashChange: 0,
            notes: $"Holdings replaced from broker file ({portfolio.Holdings.Count} → {parsedHoldings.Count})");
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

    /// <summary>
    /// Portfolio totals. Value = holdings + uninvested cash; Cost and ProfitLoss are about the holdings
    /// (cash has no P/L). ProfitLoss is net of CSE selling charges for stock portfolios.
    /// </summary>
    private sealed record Valuation(
        decimal Value, decimal HoldingsValue, decimal Cash, decimal Cost, decimal ProfitLoss, decimal ProfitLossPercent,
        decimal TodayProfitLoss, decimal TodayProfitLossPercent)
    {
        public static readonly Valuation Empty = new(0, 0, 0, 0, 0, 0, 0, 0);
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

        // Cash doesn't move day to day, but it is part of yesterday's portfolio value too.
        var cash = p.CashBalance;
        var previousTotal = previousValue + cash;

        return new Valuation(
            value + cash, value, cash, cost, pnl,
            cost > 0 ? pnl / cost * 100 : 0,
            today,
            previousTotal > 0 ? today / previousTotal * 100 : 0);
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
        HoldingsValue          = Math.Round(v.HoldingsValue, 4),
        CashBalance            = Math.Round(v.Cash, 4),
        TotalProfitLoss        = Math.Round(v.ProfitLoss, 4),
        TotalProfitLossPercent = Math.Round(v.ProfitLossPercent, 2),
        TodayProfitLoss        = Math.Round(v.TodayProfitLoss, 4),
        TodayProfitLossPercent = Math.Round(v.TodayProfitLossPercent, 2),
        HoldingCount           = p.Holdings.Count,
        CreatedAt              = p.CreatedAt
    };

    private static PortfolioDetailDto ToDetailDto(
        Portfolio portfolio, List<HoldingDto> holdings, Valuation v, decimal realizedProfitLoss)
        => new()
        {
            Id                     = portfolio.Id,
            Name                   = portfolio.Name,
            Type                   = portfolio.Type.ToString(),
            BaseCurrency           = portfolio.BaseCurrency,
            Description            = portfolio.Description,
            TotalValue             = Math.Round(v.Value, 4),
            HoldingsValue          = Math.Round(v.HoldingsValue, 4),
            CashBalance            = Math.Round(v.Cash, 4),
            RealizedProfitLoss     = Math.Round(realizedProfitLoss, 4),
            TotalCost              = Math.Round(v.Cost, 4),
            TotalProfitLoss        = Math.Round(v.ProfitLoss, 4),
            TotalProfitLossPercent = Math.Round(v.ProfitLossPercent, 2),
            TodayProfitLoss        = Math.Round(v.TodayProfitLoss, 4),
            TodayProfitLossPercent = Math.Round(v.TodayProfitLossPercent, 2),
            CreatedAt              = portfolio.CreatedAt,
            Holdings               = holdings
        };
}
