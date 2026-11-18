// ─────────────────────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class CreatePortfolioRequest
{
    public string Name { get; set; } = null!;
    public PortfolioType Type { get; set; }         // Stocks or Crypto
    public string BaseCurrency { get; set; } = "LKR"; // "LKR" or "USDT"
    public string? Description { get; set; }
}

public class UpdatePortfolioRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class AddHoldingRequest
{
    public string Symbol { get; set; } = null!;      // "ABAN.N0000" or "BTCUSDT"
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }     // In portfolio BaseCurrency
    public string? Notes { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class HoldingDto
{
    public int Id { get; set; }
    public string Symbol { get; set; } = null!;
    public string? Name { get; set; }                 // Company name (CSE stocks)
    public string AssetType { get; set; } = null!;    // "Stock" or "Crypto"
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }
    /// <summary>Cost per share incl. the 1.12% buy fee: AverageBuyPrice × 1.0112. Null for crypto.</summary>
    public decimal? CostPerShare { get; set; }
    /// <summary>
    /// Break-even selling price (ATrad formula): (buy value + 1.12% buy fees) / (qty × (1 − 1.12% sell fees)).
    /// Null for crypto.
    /// </summary>
    public decimal? BreakEvenPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    /// <summary>Previous session close (CSE) or 24h open (crypto).</summary>
    public decimal PreviousClose { get; set; }
    public decimal DayChange { get; set; }            // CurrentPrice - PreviousClose
    public decimal DayChangePercent { get; set; }
    public decimal DayProfitLoss { get; set; }        // Quantity * DayChange
    public decimal CurrentValue { get; set; }         // Quantity * CurrentPrice
    public decimal WeightPercent { get; set; }        // CurrentValue / portfolio value
    public decimal ProfitLoss { get; set; }           // Stocks: after buy + sell charges (0 at BreakEvenPrice)
    public decimal ProfitLossPercent { get; set; }
    // Today's market activity for the security (CSE stocks; null until the first tradeSummary poll)
    public long? ShareVolume { get; set; }
    public long? TradeVolume { get; set; }
    public decimal? Turnover { get; set; }
    public string? Notes { get; set; }
}

public class PortfolioDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;         // "Stocks" or "Crypto"
    public string BaseCurrency { get; set; } = null!;
    public string? Description { get; set; }
    public decimal TotalValue { get; set; }           // Sum of all holding CurrentValues
    public decimal TotalCost { get; set; }            // Sum of Quantity * AverageBuyPrice (+ 1.12% buy fees for stocks)
    public decimal TotalProfitLoss { get; set; }
    public decimal TotalProfitLossPercent { get; set; }
    public decimal TodayProfitLoss { get; set; }      // Sum of holdings' DayProfitLoss
    public decimal TodayProfitLossPercent { get; set; } // vs the portfolio's previous-close value
    public DateTime CreatedAt { get; set; }
    public List<HoldingDto> Holdings { get; set; } = new();
}

public class CurrencyExchangeRateDto
{
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ParsedHoldingDto
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }
}

public class PortfolioSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string BaseCurrency { get; set; } = null!;
    public string? Description { get; set; }
    public decimal TotalValue { get; set; }           // Native currency total
    public decimal TotalProfitLoss { get; set; }
    public decimal TotalProfitLossPercent { get; set; }
    public decimal TodayProfitLoss { get; set; }
    public decimal TodayProfitLossPercent { get; set; }
    public int HoldingCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NetWorthOverviewDto
{
    public decimal TotalNetWorthLkr { get; set; }
    public decimal TotalNetWorthUsdt { get; set; }
    public decimal TotalProfitLossLkr { get; set; }
    public decimal TotalProfitLossUsdt { get; set; }
    public decimal UsdtToLkrRate { get; set; }
    public decimal LkrToUsdtRate { get; set; }
    public List<PortfolioSummaryDto> Portfolios { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// Snapshot history (portfolio value over time)
// ─────────────────────────────────────────────────────────────────────────────

public class PortfolioHistoryPointDto
{
    public DateTime CapturedAt { get; set; }          // UTC, top of the hour
    public decimal ValueLkr { get; set; }
    public decimal ValueUsdt { get; set; }
    public decimal ProfitLossLkr { get; set; }
    public decimal ProfitLossUsdt { get; set; }
}

public class PortfolioHistoryDto
{
    public string Range { get; set; } = "1M";         // 1D, 1W, 1M, 3M, 1Y, ALL
    public int? PortfolioId { get; set; }             // null = aggregated net worth
    public string Interval { get; set; } = "hour";    // "hour" or "day" (down-sampled)
    public decimal StartValueLkr { get; set; }
    public decimal EndValueLkr { get; set; }
    public decimal ChangeLkr { get; set; }
    public decimal ChangePercent { get; set; }
    public List<PortfolioHistoryPointDto> Points { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// Portfolio insights (signals + upcoming dividends for held stocks)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Rule-based signals for one CSE holding, from its ~1 year of daily CSE prices.</summary>
public class HoldingSignalsDto
{
    public string Symbol { get; set; } = null!;
    public int Sessions { get; set; }                 // daily bars used
    public string Trend { get; set; } = "INSUFFICIENT_DATA"; // UPTREND | DOWNTREND | MIXED | INSUFFICIENT_DATA
    public decimal? Sma50 { get; set; }
    public decimal? Sma200 { get; set; }
    public decimal? Rsi14 { get; set; }
    public string? RsiState { get; set; }             // OVERBOUGHT (>=70) | OVERSOLD (<=30) | null
    public decimal? High52w { get; set; }
    public decimal? Low52w { get; set; }
    public decimal? RangePosition { get; set; }       // 0 = at 52w low, 100 = at 52w high
    public bool NearHigh52w { get; set; }             // within 5% of the 52-week high
    public bool NearLow52w { get; set; }              // within 5% of the 52-week low
    public decimal? VolumeRatio { get; set; }         // last session volume / 20-session average
    public bool VolumeSpike { get; set; }             // VolumeRatio >= 2
}

public class HoldingDividendDto
{
    public string Symbol { get; set; } = null!;
    public string? CompanyName { get; set; }
    public decimal DividendPerShare { get; set; }
    public decimal Quantity { get; set; }
    public decimal ExpectedPayout { get; set; }       // Quantity * DividendPerShare (before tax)
    public DateTime RecordDate { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? PaymentDateRaw { get; set; }
    public string? FinancialYear { get; set; }
}

public class PortfolioInsightsDto
{
    public int PortfolioId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<HoldingSignalsDto> Signals { get; set; } = new();
    public List<HoldingDividendDto> UpcomingDividends { get; set; } = new();
    public decimal ExpectedDividendIncome { get; set; }
}
