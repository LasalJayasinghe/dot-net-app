public class TickerData
{
    public string Symbol { get; set; } = "";
    public decimal LastPrice { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal PriceChange { get; set; }
    public decimal PriceChangePercent { get; set; }
    public decimal High24h { get; set; }
    public decimal Low24h { get; set; }
    public decimal Volume24h { get; set; }
    public decimal QuoteVolume24h { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
