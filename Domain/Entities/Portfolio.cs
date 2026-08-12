using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Portfolio
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!; // e.g. "My CSE Growth", "Binance Spot"

    [Required]
    public PortfolioType Type { get; set; } // Stocks, Crypto

    [Required]
    [MaxLength(10)]
    public string BaseCurrency { get; set; } = "LKR"; // "LKR" for Stocks, "USDT" for Crypto

    [MaxLength(255)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
    public ICollection<PortfolioHolding> Holdings { get; set; } = new List<PortfolioHolding>();
}

public enum PortfolioType
{
    Stocks = 1,
    Crypto = 2
}
