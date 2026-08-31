using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dotnetApp.Domain.Entities;

public class Dividend
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    public DateTime DateOfAnnouncement { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal VotingDivPerShare { get; set; }

    [Column(TypeName = "decimal(28,8)")]
    public decimal NonVotingDivPerShare { get; set; }

    [MaxLength(50)]
    public string? FinancialYear { get; set; }

    public DateTime RecordDate { get; set; }

    public string? Remarks { get; set; }

    // Kept as text because CSE payload currently provides these dates as raw strings.
    public string? PaymentDate { get; set; }
    public string? AgmDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
