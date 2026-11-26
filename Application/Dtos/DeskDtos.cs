using System;
using System.Collections.Generic;

namespace dotnetApp.Application.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// Trading desk — GET /api/desk/overview and GET /api/desk/symbol/{symbol}
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Everything the desk shows for one market, in one call.</summary>
public class DeskOverviewDto
{
    /// <summary>"cse" or "crypto".</summary>
    public string Market { get; set; } = "cse";

    /// <summary>Base currency of the market: LKR or USDT.</summary>
    public string Currency { get; set; } = "LKR";

    /// <summary>When the board's prices were taken (UTC).</summary>
    public DateTime? PricesAsOf { get; set; }

    public DeskSessionDto Session { get; set; } = new();

    /// <summary>The ASPI panel (CSE only).</summary>
    public DeskCseMarketDto? Cse { get; set; }

    /// <summary>The compact strip (crypto only).</summary>
    public DeskCryptoMarketDto? Crypto { get; set; }

    /// <summary>Every listed stock (CSE) or tracked pair (crypto).</summary>
    public List<DeskBoardRowDto> Board { get; set; } = new();

    /// <summary>Every holding across the user's portfolios in this market, summed per symbol.</summary>
    public List<DeskPositionDto> Portfolio { get; set; } = new();
    public DeskPortfolioTotalsDto PortfolioTotals { get; set; } = new();

    public List<DeskBoardRowDto> Watchlist { get; set; } = new();

    /// <summary>Active alerts within 2% of their target, nearest first.</summary>
    public List<DeskAlertDto> NearAlerts { get; set; } = new();

    /// <summary>Active alerts per symbol (for the 🔔 badges).</summary>
    public Dictionary<string, int> AlertCounts { get; set; } = new();

    public DeskTodayDto Today { get; set; } = new();
}

public class DeskSessionDto
{
    public bool IsOpen { get; set; }
    public bool IsTradingDay { get; set; }

    /// <summary>Colombo time "HH:mm" (CSE only).</summary>
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }

    /// <summary>Crypto trades around the clock.</summary>
    public bool AlwaysOpen { get; set; }
}

public class DeskIndexDto
{
    public decimal Value { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
}

public class DeskIndexPointDto
{
    public DateTime At { get; set; }
    public decimal Aspi { get; set; }
    public decimal SpSl20 { get; set; }
}

/// <summary>"All ASPI data": indices, intraday line, breadth, activity, foreign flows, market valuation.</summary>
public class DeskCseMarketDto
{
    public DeskIndexDto Aspi { get; set; } = new();
    public DeskIndexDto SpSl20 { get; set; } = new();
    public List<DeskIndexPointDto> AspiIntraday { get; set; } = new();

    public int Advancers { get; set; }
    public int Decliners { get; set; }
    public int Unchanged { get; set; }

    /// <summary>Securities with at least one trade today.</summary>
    public int Traded { get; set; }

    public decimal Turnover { get; set; }
    public decimal? TurnoverAvg20d { get; set; }
    public long Trades { get; set; }
    public decimal MarketCap { get; set; }

    /// <summary>The session the daily figures below come from (the previous close until today's is published).</summary>
    public DateOnly? DailyDate { get; set; }
    public decimal? ForeignPurchase { get; set; }
    public decimal? ForeignSales { get; set; }
    public decimal? MarketPer { get; set; }
    public decimal? MarketPbv { get; set; }
    public decimal? MarketDividendYield { get; set; }
}

public class DeskCryptoMarketDto
{
    /// <summary>24h quote volume across the tracked pairs, in USDT.</summary>
    public decimal QuoteVolume24h { get; set; }
    public int Advancers { get; set; }
    public int Decliners { get; set; }
    public decimal? BtcPrice { get; set; }
    public decimal? BtcChangePercent { get; set; }
}

public class DeskBoardRowDto
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }

    /// <summary>Previous close (CSE) or 24h open (crypto).</summary>
    public decimal PreviousClose { get; set; }

    /// <summary>Shares (CSE) or base units (crypto).</summary>
    public decimal Volume { get; set; }

    /// <summary>Number of trades (CSE only).</summary>
    public long? Trades { get; set; }

    /// <summary>Turnover in LKR (CSE) or 24h quote volume in USDT (crypto).</summary>
    public decimal Turnover { get; set; }
    public decimal? MarketCap { get; set; }
}

/// <summary>One holding with its related data (summed across the user's portfolios).</summary>
public class DeskPositionDto
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }
    public decimal Price { get; set; }
    public decimal DayChangePercent { get; set; }
    public decimal DayProfitLoss { get; set; }
    public decimal Value { get; set; }
    public decimal Cost { get; set; }

    /// <summary>After fees (CSE: buy + sell charges), so 0 at the break-even price.</summary>
    public decimal ProfitLoss { get; set; }
    public decimal ProfitLossPercent { get; set; }
    public decimal BreakEvenPrice { get; set; }
    public decimal WeightPercent { get; set; }
    public decimal Volume { get; set; }
    public decimal Turnover { get; set; }

    // CSE extras
    public string? Trend { get; set; }
    public decimal? Rsi14 { get; set; }
    public decimal? PriceToEarnings { get; set; }
    public decimal? PriceToBook { get; set; }
    public DateTime? NextDividendRecordDate { get; set; }
    public decimal? NextDividendPerShare { get; set; }

    public int AlertCount { get; set; }

    /// <summary>Portfolios that hold it.</summary>
    public List<string> Portfolios { get; set; } = new();
}

public class DeskPortfolioTotalsDto
{
    public string Currency { get; set; } = "LKR";
    public decimal HoldingsValue { get; set; }
    public decimal DayProfitLoss { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal Cash { get; set; }
}

public class DeskAlertDto
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal TargetPrice { get; set; }
    public bool IsAbove { get; set; }
    public bool IsActive { get; set; }
    public bool FireOnce { get; set; }
    public decimal? CurrentPrice { get; set; }

    /// <summary>How far the price must move to trigger, in % of the current price.</summary>
    public decimal? DistancePercent { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
}

public class DeskTodayDto
{
    public List<DeskTradeDto> Trades { get; set; } = new();
    public decimal RealizedProfitLoss { get; set; }
    public List<DeskAlertDto> AlertsFired { get; set; } = new();
}

public class DeskTradeDto
{
    public DateTime At { get; set; }
    public string Portfolio { get; set; } = "";
    public string Side { get; set; } = "";
    public string Symbol { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? RealizedProfitLoss { get; set; }
}

/// <summary>The selected-stock panel.</summary>
public class DeskSymbolDto
{
    public string Market { get; set; } = "cse";
    public string Symbol { get; set; } = "";
    public string Currency { get; set; } = "LKR";
    public DeskBoardRowDto? Quote { get; set; }

    /// <summary>Summed across portfolios; null when not held.</summary>
    public DeskPositionDto? Position { get; set; }

    /// <summary>The user's portfolios in this market (for Buy / Sell), with cash and what each holds of this symbol.</summary>
    public List<DeskPortfolioOptionDto> Portfolios { get; set; } = new();

    public List<DeskAlertDto> Alerts { get; set; } = new();

    /// <summary>CSE only.</summary>
    public StockFundamentalsDto? Fundamentals { get; set; }
}

public class DeskPortfolioOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Cash { get; set; }
    public decimal Quantity { get; set; }
}
