public class Alert
{
    public int Id { get; set; }
    public string Symbol { get; set; } = null!;
    public decimal TargetPrice { get; set; }
    public bool IsAbove { get; set; } // true for above, false for below
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = false;
    public string CreatedBy { get; set; } = null!;
}