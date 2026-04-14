public class Signal
{
    public string Type { get; set; } = ""; // BUY / SELL / HOLD

    public decimal Price { get; set; }

    public string Reason { get; set; } = "";

    public DateTime Time { get; set; } = DateTime.UtcNow;
}