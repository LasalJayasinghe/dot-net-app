public class Position
{
    public bool IsOpen { get; set; }

    public string Symbol { get; set; } = "BTCUSDT";

    public decimal EntryPrice { get; set; }

    public decimal Quantity { get; set; }

    public DateTime? EntryTime { get; set; }

    public decimal? StopLoss { get; set; }

    public decimal? TakeProfit { get; set; }

    public decimal GetUnrealizedPnL(decimal currentPrice)
    {
        if (!IsOpen) return 0;

        return (currentPrice - EntryPrice) * Quantity;
    }

    public void Open(decimal entryPrice, decimal quantity)
    {
        IsOpen = true;
        EntryPrice = entryPrice;
        Quantity = quantity;
        EntryTime = DateTime.UtcNow;
    }

    public void Close()
    {
        IsOpen = false;
        EntryPrice = 0;
        Quantity = 0;
        EntryTime = null;
        StopLoss = null;
        TakeProfit = null;
    }
}