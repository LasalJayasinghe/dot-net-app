using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dotnetApp.Domain.Entities;

public class Dividend
{
    /// <summary>Surrogate key. Never assigned from the CSE payload - see <see cref="CseAnnouncementId"/>.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>The CSE announcement id this row was ingested from. Unique; used as the upsert key.</summary>
    public int CseAnnouncementId { get; set; }

    /// <summary>Full CSE ticker as published, e.g. <c>CSLK.N0000</c>.</summary>
    [Required]
    [MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Ticker without the CSE class suffix, e.g. <c>CSLK</c>. Indexed so symbol lookups are a
    /// plain equality match instead of an <c>OR ... LIKE</c> pair that no index can serve.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string BaseSymbol { get; set; } = string.Empty;

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

    /// <summary>Colombo calendar date at midnight. See <c>ColomboTime</c> for why this is not a UTC instant.</summary>
    public DateTime RecordDate { get; set; }

    public string? Remarks { get; set; }

    /// <summary>Parsed payment date, or null when <see cref="PaymentDateRaw"/> could not be parsed.</summary>
    public DateTime? PaymentDate { get; set; }

    /// <summary>Parsed AGM date, or null when <see cref="AgmDateRaw"/> could not be parsed.</summary>
    public DateTime? AgmDate { get; set; }

    // The raw CSE strings are retained so an unparseable value is never silently lost.
    [MaxLength(100)]
    public string? PaymentDateRaw { get; set; }

    [MaxLength(100)]
    public string? AgmDateRaw { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
