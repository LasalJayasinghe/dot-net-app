public class MarketStatus
{
    public int Id { get; set; }
    public bool IsTradingDay { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly OpenTime { get; set; } = TimeOnly.Parse("09:30:00");
    public TimeOnly CloseTime { get; set; } = TimeOnly.Parse("15:30:00");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}