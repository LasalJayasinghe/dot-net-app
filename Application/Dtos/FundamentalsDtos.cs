using System;

namespace dotnetApp.Application.DTOs;

/// <summary>A fundamental figure with how far it can be trusted.</summary>
public class FundamentalValueDto
{
    public decimal? Value { get; set; }

    /// <summary>"verified", "unverified", "manual", "missing" or "partial".</summary>
    public string Status { get; set; } = "missing";

    /// <summary>Why a value is missing or how it was derived (shown as a tooltip).</summary>
    public string? Note { get; set; }
}

/// <summary>The core fundamentals of one CSE stock: valuation, profitability, ownership and market risk.</summary>
public class StockFundamentalsDto
{
    public string Symbol { get; set; } = "";
    public decimal? Price { get; set; }

    // ── Valuation ──
    public FundamentalValueDto NavPerShare { get; set; } = new();
    public FundamentalValueDto PriceToBook { get; set; } = new();
    public FundamentalValueDto EpsTtm { get; set; } = new();
    public FundamentalValueDto PriceToEarnings { get; set; } = new();
    public FundamentalValueDto DividendYield { get; set; } = new();

    // ── Profitability ──
    /// <summary>Trailing EPS ÷ latest NAV per share, in % (return on closing book).</summary>
    public FundamentalValueDto Roe { get; set; } = new();

    // ── Ownership & market ──
    public FundamentalValueDto PublicFloatPercent { get; set; } = new();
    public decimal? MarketCap { get; set; }
    public long? SharesInIssue { get; set; }
    public decimal? BetaAspi { get; set; }
    public decimal? BetaSl20 { get; set; }
    public decimal? High52Week { get; set; }
    public decimal? Low52Week { get; set; }

    // ── Source ──
    /// <summary>The report the NAV / EPS came from.</summary>
    public string? ReportTitle { get; set; }
    public DateOnly? ReportPeriodEnd { get; set; }
    public string? ReportUrl { get; set; }

    /// <summary>False until the first report for this stock has been read (backfill still running).</summary>
    public bool HasReports { get; set; }
}

/// <summary>Admin override for a report's figures when the PDF can't be read reliably.</summary>
public class FundamentalsOverrideDto
{
    public decimal? NavPerShare { get; set; }
    public int? EpsYtdMonths { get; set; }
    public decimal? EpsYtdCurrent { get; set; }
    public decimal? EpsYtdPrior { get; set; }
    public decimal? EpsLastFullYear { get; set; }
}
