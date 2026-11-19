using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dotnetApp.Domain.Entities;

/// <summary>Where a daily row came from. A TradeSummary row is richer, so a Chart row never overwrites it.</summary>
public static class CseHistorySource
{
    /// <summary>Recorded after the close from cse.lk tradeSummary: full OHLC, volume, trades, turnover, market cap.</summary>
    public const string TradeSummary = "TradeSummary";

    /// <summary>Backfilled from cse.lk chart history: high/low/close/volume only (no open, ~1 year deep).</summary>
    public const string Chart = "Chart";
}

/// <summary>
/// One CSE security's trading day. Permanent history (rows are never deleted, also for suspended or
/// delisted securities) so strategies can be backtested on CSE data later.
/// </summary>
public class CsePriceDaily
{
    [Key]
    public long Id { get; set; }

    /// <summary>Full CSE code, e.g. <c>JKH.N0000</c>.</summary>
    [Required, MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>CSE internal security id (used by the chart API).</summary>
    public int? StockId { get; set; }

    /// <summary>Trading day in Colombo time.</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>Null for chart-backfilled days (CSE chart history has no open).</summary>
    [Column(TypeName = "decimal(18,4)")] public decimal? Open { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal High { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal Low { get; set; }

    /// <summary>Official CSE closing price.</summary>
    [Column(TypeName = "decimal(18,4)")] public decimal Close { get; set; }

    [Column(TypeName = "decimal(18,4)")] public decimal? PreviousClose { get; set; }

    public long ShareVolume { get; set; }

    /// <summary>Number of trades (null for chart-backfilled days).</summary>
    public long? Trades { get; set; }

    [Column(TypeName = "decimal(22,2)")] public decimal? Turnover { get; set; }
    [Column(TypeName = "decimal(24,2)")] public decimal? MarketCap { get; set; }

    /// <summary>tradeSummary <c>status</c> code (null for chart rows).</summary>
    public int? Status { get; set; }

    [Required, MaxLength(16)]
    public string Source { get; set; } = CseHistorySource.Chart;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Daily close of a CSE index (ASPI, SNPSL20).</summary>
public class CseIndexDaily
{
    [Key]
    public long Id { get; set; }

    /// <summary><c>ASPI</c> or <c>SNPSL20</c>.</summary>
    [Required, MaxLength(16)]
    public string IndexCode { get; set; } = string.Empty;

    public DateOnly TradeDate { get; set; }

    [Column(TypeName = "decimal(18,4)")] public decimal Close { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? High { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Low { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Change { get; set; }
    [Column(TypeName = "decimal(12,6)")] public decimal? ChangePercent { get; set; }

    [Required, MaxLength(16)]
    public string Source { get; set; } = CseHistorySource.Chart;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Market-wide day totals from cse.lk dailyMarketSummery (only the last 2 days are served, so this
/// can't be backfilled). Key columns are typed; the full payload is kept in <see cref="RawJson"/>.
/// </summary>
public class CseMarketDaily
{
    [Key]
    public long Id { get; set; }

    public DateOnly TradeDate { get; set; }

    [Column(TypeName = "decimal(22,2)")] public decimal? MarketTurnover { get; set; }
    public long? MarketTrades { get; set; }
    [Column(TypeName = "decimal(22,2)")] public decimal? EquityForeignPurchase { get; set; }
    [Column(TypeName = "decimal(22,2)")] public decimal? EquityForeignSales { get; set; }
    [Column(TypeName = "decimal(26,2)")] public decimal? MarketCap { get; set; }
    public int? ListedCompanies { get; set; }
    public int? TradedCompanies { get; set; }

    /// <summary>ASPI close.</summary>
    [Column(TypeName = "decimal(18,4)")] public decimal? Asi { get; set; }

    /// <summary>S&amp;P SL20 close.</summary>
    [Column(TypeName = "decimal(18,4)")] public decimal? SpSl20 { get; set; }

    /// <summary>Market price/earnings, price/book and dividend yield (%).</summary>
    [Column(TypeName = "decimal(10,4)")] public decimal? Per { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal? Pbv { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal? DividendYield { get; set; }

    /// <summary>The full dailyMarketSummery row as returned by CSE.</summary>
    [Column(TypeName = "longtext")]
    public string RawJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Every CSE security ever seen in tradeSummary, with its internal id. Lets the backfill/gap repair
/// reach securities that didn't trade today, and keeps delisted ones known (no survivorship bias).
/// </summary>
public class CseSecurity
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    public int StockId { get; set; }

    [MaxLength(255)]
    public string? Name { get; set; }

    public DateOnly FirstSeen { get; set; }
    public DateOnly LastSeen { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
