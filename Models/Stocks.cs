using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
public class Stocks
{
    [Key]
    public int Id { get; set; } // Internal DB ID (never from API)

    [Required]
    [MaxLength(20)]
    public string Symbol { get; set; } = null!; // ABAN.N0000 (UNIQUE)

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = null!;

    [Column(TypeName = "decimal(18,4)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Change { get; set; }

    [Column(TypeName = "decimal(10,6)")]
    public decimal PercentageChange { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal High { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Low { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PreviousClose { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ClosingPrice { get; set; }

}