public class Alert
{
    public int Id { get; set; }
    public string Symbol { get; set; } = null!;
    public decimal TargetPrice { get; set; }
    public bool IsAbove { get; set; } // true for above, false for below
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = false;
    public string CreatedBy { get; set; } = null!;

    /// <summary>
    /// Switch the alert off after it first notifies. Otherwise it notifies on every pass while the
    /// condition holds. New alerts default to true; alerts created before this existed are false.
    /// </summary>
    public bool FireOnce { get; set; }

    /// <summary>UTC time it last notified (shown in the desk's "fired today").</summary>
    public DateTime? LastTriggeredAt { get; set; }
}