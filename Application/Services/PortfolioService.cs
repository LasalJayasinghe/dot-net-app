using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using dotnetApp.Controllers.Api;

namespace dotnetApp.Application.Services;

/// <summary>
/// Manages user portfolios (Stocks / Crypto) and calculates live valuations
/// and cross-currency Net Worth aggregation (USDT → LKR).
/// </summary>
public class PortfolioService
{
    private readonly AppDbContext  _db;
    private readonly BinanceService _binance;
    private readonly ILogger<PortfolioService> _logger;

    public PortfolioService(AppDbContext db, BinanceService binance, ILogger<PortfolioService> logger)
    {
        _db      = db;
        _binance = binance;
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

        return ToDetailDto(portfolio, new List<HoldingDto>(), 0, 0, 0, 0);
    }

    public async Task<List<PortfolioSummaryDto>> GetUserPortfoliosAsync(string userId, PortfolioType? filter = null)
    {
        var query = _db.Portfolios
            .Include(p => p.Holdings)
            .Where(p => p.UserId == userId);

        if (filter.HasValue)
            query = query.Where(p => p.Type == filter.Value);

        var portfolios = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        var result = new List<PortfolioSummaryDto>();
        foreach (var p in portfolios)
        {
            var (totalValue, totalCost) = await CalcPortfolioValueAsync(p);
            decimal pnl = totalValue - totalCost;
            if (p.Type == PortfolioType.Stocks)
            {
                decimal salesCommission = totalValue * 1.12m / 100m;
                pnl = (totalValue - salesCommission) - totalCost;
            }
            decimal pnlPercent = totalCost > 0 ? (pnl / totalCost) * 100 : 0;

            result.Add(new PortfolioSummaryDto
            {
                Id                   = p.Id,
                Name                 = p.Name,
                Type                 = p.Type.ToString(),
                BaseCurrency         = p.BaseCurrency,
                Description          = p.Description,
                TotalValue           = Math.Round(totalValue, 4),
                TotalProfitLoss      = Math.Round(pnl, 4),
                TotalProfitLossPercent = Math.Round(pnlPercent, 2),
                HoldingCount         = p.Holdings.Count,
                CreatedAt            = p.CreatedAt
            });
        }
        return result;
    }

    public async Task<PortfolioDetailDto?> GetPortfolioDetailAsync(string userId, int portfolioId)
    {
        var portfolio = await _db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

        if (portfolio == null) return null;

        var holdingDtos = await BuildHoldingDtosAsync(portfolio);
        decimal totalValue = holdingDtos.Sum(h => h.CurrentValue);
        decimal totalCost  = holdingDtos.Sum(h => h.Quantity * h.AverageBuyPrice);
        decimal pnl = totalValue - totalCost;
        if (portfolio.Type == PortfolioType.Stocks)
        {
            decimal salesCommission = totalValue * 1.12m / 100m;
            pnl = (totalValue - salesCommission) - totalCost;
        }
        decimal pnlPercent = totalCost > 0 ? (pnl / totalCost) * 100 : 0;

        return ToDetailDto(portfolio, holdingDtos, totalValue, totalCost, pnl, pnlPercent);
    }

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

        // Return the holding DTO with current price
        decimal currentPrice = await GetCurrentPriceAsync(symbol, portfolio.Type);
        var h = existing ?? portfolio.Holdings.Last();
        var dto = BuildHoldingDto(h, currentPrice);
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

        decimal totalNetWorthLkr = 0;
        decimal totalNetWorthUsdt = 0;
        decimal totalPnlLkr      = 0;
        decimal totalPnlUsdt     = 0;
        var summaries = new List<PortfolioSummaryDto>();

        foreach (var p in portfolios)
        {
            var (totalValue, totalCost) = await CalcPortfolioValueAsync(p);
            decimal pnl = totalValue - totalCost;
            if (p.Type == PortfolioType.Stocks)
            {
                decimal salesCommission = totalValue * 1.12m / 100m;
                pnl = (totalValue - salesCommission) - totalCost;
            }
            decimal pnlPercent = totalCost > 0 ? (pnl / totalCost) * 100 : 0;

            // Convert to display currency
            decimal rateToLkr = GetConversionRate(p.BaseCurrency, "LKR", usdtToLkr, lkrToUsdt);
            decimal rateToUsdt = GetConversionRate(p.BaseCurrency, "USDT", usdtToLkr, lkrToUsdt);
            
            totalNetWorthLkr += totalValue * rateToLkr;
            totalNetWorthUsdt += totalValue * rateToUsdt;
            totalPnlLkr      += pnl * rateToLkr;
            totalPnlUsdt     += pnl * rateToUsdt;

            summaries.Add(new PortfolioSummaryDto
            {
                Id                     = p.Id,
                Name                   = p.Name,
                Type                   = p.Type.ToString(),
                BaseCurrency           = p.BaseCurrency,
                Description            = p.Description,
                TotalValue             = Math.Round(totalValue, 4),
                TotalProfitLoss        = Math.Round(pnl, 4),
                TotalProfitLossPercent = Math.Round(pnlPercent, 2),
                HoldingCount           = p.Holdings.Count,
                CreatedAt              = p.CreatedAt
            });
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

    private async Task<(decimal totalValue, decimal totalCost)> CalcPortfolioValueAsync(Portfolio portfolio)
    {
        decimal totalValue = 0;
        decimal totalCost  = 0;
        foreach (var h in portfolio.Holdings)
        {
            decimal price = await GetCurrentPriceAsync(h.Symbol, portfolio.Type);
            totalValue += h.Quantity * price;
            totalCost  += h.Quantity * h.AverageBuyPrice;
        }
        return (totalValue, totalCost);
    }

    private async Task<List<HoldingDto>> BuildHoldingDtosAsync(Portfolio portfolio)
    {
        var result = new List<HoldingDto>();
        foreach (var h in portfolio.Holdings)
        {
            decimal price = await GetCurrentPriceAsync(h.Symbol, portfolio.Type);
            result.Add(BuildHoldingDto(h, price));
        }
        return result;
    }

    private static HoldingDto BuildHoldingDto(PortfolioHolding h, decimal currentPrice)
    {
        decimal currentValue = h.Quantity * currentPrice;
        decimal cost         = h.Quantity * h.AverageBuyPrice;
        decimal pnl = currentValue - cost;
        if (h.AssetType == AssetType.Stock)
        {
            decimal salesCommission = currentValue * 1.12m / 100m;
            pnl = (currentValue - salesCommission) - cost;
        }
        decimal pnlPct = cost > 0 ? (pnl / cost) * 100 : 0;

        return new HoldingDto
        {
            Id                  = h.Id,
            Symbol              = h.Symbol,
            AssetType           = h.AssetType.ToString(),
            Quantity            = h.Quantity,
            AverageBuyPrice     = h.AverageBuyPrice,
            CurrentPrice        = currentPrice,
            CurrentValue        = Math.Round(currentValue, 4),
            ProfitLoss          = Math.Round(pnl, 4),
            ProfitLossPercent   = Math.Round(pnlPct, 2),
            Notes               = h.Notes
        };
    }

    /// <summary>
    /// Resolves current live price. 
    /// - Crypto: uses BinanceService in-memory price feed (WebSocket).
    /// - Stocks: reads the latest price stored in the DB (updated by background job).
    /// </summary>
    private async Task<decimal> GetCurrentPriceAsync(string symbol, PortfolioType type)
    {
        if (type == PortfolioType.Crypto)
        {
            var ticker = _binance.GetTicker(symbol);
            return ticker?.LastPrice ?? _binance.Prices.GetValueOrDefault(symbol, 0);
        }

        // Stock — read from DB cache (updated by polling job)
        var stock = await _db.Stocks.AsNoTracking().FirstOrDefaultAsync(s => s.Symbol == symbol);
        if (stock == null) return 0;
        return stock.Price > 0 ? stock.Price : stock.ClosingPrice;
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

    private static PortfolioDetailDto ToDetailDto(
        Portfolio portfolio,
        List<HoldingDto> holdings,
        decimal totalValue, decimal totalCost, decimal pnl, decimal pnlPercent)
        => new()
        {
            Id                     = portfolio.Id,
            Name                   = portfolio.Name,
            Type                   = portfolio.Type.ToString(),
            BaseCurrency           = portfolio.BaseCurrency,
            Description            = portfolio.Description,
            TotalValue             = Math.Round(totalValue, 4),
            TotalCost              = Math.Round(totalCost, 4),
            TotalProfitLoss        = Math.Round(pnl, 4),
            TotalProfitLossPercent = Math.Round(pnlPercent, 2),
            CreatedAt              = portfolio.CreatedAt,
            Holdings               = holdings
        };
}
