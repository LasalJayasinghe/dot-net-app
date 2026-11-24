using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dotnetApp.Domain.Entities;

/// <summary>How much we trust a figure read from a financial report.</summary>
public enum FundamentalStatus
{
    /// <summary>Nothing usable was found.</summary>
    NotFound = 0,

    /// <summary>Found, but it could not be cross-checked (or the cross-check disagreed).</summary>
    Unverified = 1,

    /// <summary>Found and confirmed by an independent figure in the same report.</summary>
    Verified = 2,

    /// <summary>Entered by hand (an admin override); never replaced by re-extraction.</summary>
    Manual = 3,
}

/// <summary>
/// One CSE interim financial statement (as listed by cse.lk <c>financials</c>) and the headline figures
/// read from its PDF by <c>CseReportExtractor</c>. Fundamentals (NAV, P/BV, trailing EPS, P/E, ROE) are
/// computed from these rows plus live prices.
/// </summary>
public class CseFinancialReport
{
    [Key]
    public int Id { get; set; }

    /// <summary>Voting-share code, e.g. <c>JKH.N0000</c>.</summary>
    [Required, MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>The report's id in the cse.lk financials listing. Unique; the upsert key.</summary>
    public int CseReportId { get; set; }

    /// <summary>As listed, e.g. "Interim Financial Statements for the Quarter ended 30th September 2026".</summary>
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Path on cdn.cse.lk (may contain spaces; URL-encode when building a link).</summary>
    [MaxLength(300)]
    public string PdfPath { get; set; } = string.Empty;

    /// <summary>Period end parsed from <see cref="Title"/>; null when the title names no date.</summary>
    public DateOnly? PeriodEnd { get; set; }

    public DateTime? UploadedAt { get; set; }

    /// <summary>All share classes (voting + non-voting) used for the cross-checks.</summary>
    public long? SharesInIssue { get; set; }

    // ── NAV ──
    [Column(TypeName = "decimal(18,4)")] public decimal? NavPerShare { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? NavFromEquity { get; set; }
    [MaxLength(10)] public string? NavEntity { get; set; }
    public FundamentalStatus NavStatus { get; set; }

    // ── EPS (basic, attributable to the parent's owners) ──
    [MaxLength(10)] public string? EpsEntity { get; set; }
    public FundamentalStatus EpsStatus { get; set; }
    public int? EpsYtdMonths { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? EpsYtdCurrent { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? EpsYtdPrior { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? EpsQuarterCurrent { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? EpsQuarterPrior { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? EpsLastFullYear { get; set; }

    [Column(TypeName = "decimal(9,4)")] public decimal? PublicFloatPercent { get; set; }

    /// <summary>Bumped when the extractor improves, so older rows are re-read.</summary>
    public int ExtractorVersion { get; set; }

    /// <summary>The extractor's trail: what it found and why each status was given.</summary>
    [Column(TypeName = "longtext")]
    public string? Notes { get; set; }

    /// <summary>Set when the PDF could not be downloaded or read.</summary>
    [MaxLength(500)]
    public string? Error { get; set; }

    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
