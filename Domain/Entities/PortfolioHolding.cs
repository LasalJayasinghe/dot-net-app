using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class PortfolioHolding
{
    [Key]
    public int Id { get; set; }

    public int PortfolioId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Symbol { get; set; } = null!; // e.g. "ABAN.N0000" or "BTCUSDT"

    [Required]
    public AssetType AssetType { get; set; } // Stock, Crypto

    /// <summary>
    /// Quantity — supports fractional crypto (e.g. 0.00152500 BTC) and integer stock shares.
    /// </summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Average purchase price stored in the portfolio's BaseCurrency (LKR for Stocks, USDT for Crypto).
    /// </summary>
    [Column(TypeName = "decimal(28,8)")]
    public decimal AverageBuyPrice { get; set; }

    [MaxLength(255)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Portfolio Portfolio { get; set; } = null!;
}

public enum AssetType
{
    Stock = 1,
    Crypto = 2
}
