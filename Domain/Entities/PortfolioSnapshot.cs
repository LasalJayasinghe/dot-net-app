using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// An hourly point-in-time valuation, written by <c>PortfolioSnapshotJob</c>, that powers
/// the "portfolio value over time" graph.
///
/// Two flavours of row are written per capture:
///   • PortfolioId == null → the user's aggregated net worth across every portfolio.
///   • PortfolioId != null → that single portfolio's valuation.
///
/// Values are always stored in BOTH display currencies so the graph never has to
/// re-convert using a rate that has since moved.
/// </summary>
public class PortfolioSnapshot
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = null!;

    /// <summary>Null for the aggregated net-worth row.</summary>
    public int? PortfolioId { get; set; }

    /// <summary>UTC, truncated to the top of the hour — this is the graph's x-axis.</summary>
    public DateTime CapturedAt { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal TotalValueLkr { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal TotalValueUsdt { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal TotalProfitLossLkr { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal TotalProfitLossUsdt { get; set; }

    /// <summary>The USDT→LKR rate used for this capture, kept for auditability.</summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal UsdtToLkrRate { get; set; }

    public int HoldingCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
    public Portfolio? Portfolio { get; set; }
}
