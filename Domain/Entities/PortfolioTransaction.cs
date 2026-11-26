using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>What a <see cref="PortfolioTransaction"/> did.</summary>
public enum PortfolioTransactionType
{
    /// <summary>A holding that existed before the ledger started (written once by the migration).</summary>
    Opening = 0,

    /// <summary>Bought shares/coins; paid from cash unless added as an existing position.</summary>
    Buy = 1,

    /// <summary>Sold some or all of a holding; proceeds go to cash.</summary>
    Sell = 2,

    /// <summary>Cash paid into the account.</summary>
    Deposit = 3,

    /// <summary>Cash taken out of the account.</summary>
    Withdrawal = 4,

    /// <summary>Cash balance corrected to match the broker / exchange.</summary>
    CashAdjustment = 5,

    /// <summary>A holding's quantity or average price corrected, removed, or replaced by a file sync.</summary>
    HoldingAdjustment = 6,
}

/// <summary>
/// One entry in a portfolio's ledger. Holdings and <see cref="Portfolio.CashBalance"/> are kept as
/// current state; every change to them is recorded here, so sells carry their realised P/L.
/// </summary>
public class PortfolioTransaction
{
    [Key]
    public int Id { get; set; }

    public int PortfolioId { get; set; }

    public PortfolioTransactionType Type { get; set; }

    /// <summary>Null for cash-only entries.</summary>
    [MaxLength(30)]
    public string? Symbol { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal? Quantity { get; set; }

    /// <summary>Trade price per unit in the portfolio's base currency (before fees).</summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal? Price { get; set; }

    /// <summary>Brokerage / exchange charges on this trade.</summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal Fees { get; set; }

    /// <summary>Signed effect on the cash balance (negative for a paid buy or a withdrawal).</summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal CashChange { get; set; }

    /// <summary>Cash balance after this entry.</summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal CashBalanceAfter { get; set; }

    /// <summary>Sells only: net proceeds − cost basis of the units sold (after fees).</summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal? RealizedProfitLoss { get; set; }

    [MaxLength(255)]
    public string? Notes { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Portfolio Portfolio { get; set; } = null!;
}
