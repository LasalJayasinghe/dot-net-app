using System;
using System.Collections.Generic;

namespace dotnetApp.Application.Models;

public class PaperTrade
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Symbol { get; set; } = "BTCUSDT";
    public string Side { get; set; } = ""; // BUY / SELL
    public decimal Quantity { get; set; }
    public decimal ExecutionPrice { get; set; }
    public decimal GrossValue { get; set; }
    public decimal Fee { get; set; }
    public decimal NetValue { get; set; }
    public DateTime Timestamp { get; set; }
    public string Reason { get; set; } = "";
}

public class PaperTradingState
{
    public decimal UsdBalance { get; set; } = 1000m;
    public decimal BtcBalance { get; set; } = 0m;
    
    // Position tracking
    public bool IsOpen { get; set; }
    public decimal EntryPrice { get; set; }
    public DateTime? EntryTime { get; set; }
    
    public List<PaperTrade> TradeHistory { get; set; } = new();
}
