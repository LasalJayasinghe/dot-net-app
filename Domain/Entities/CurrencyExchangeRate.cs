using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class CurrencyExchangeRate
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string FromCurrency { get; set; } = null!; // e.g. "USDT"

    [Required]
    [MaxLength(10)]
    public string ToCurrency { get; set; } = null!; // e.g. "LKR"

    /// <summary>
    /// The conversion rate. e.g. 305.50 means 1 USDT = 305.50 LKR.
    /// </summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal Rate { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
