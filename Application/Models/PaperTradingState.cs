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

    // SELL only: fee paid on the matching BUY, and P/L net of BOTH entry and exit fees.
    public decimal? EntryFee { get; set; }
    public decimal? RealizedPnl { get; set; }
}

public class PaperTradingState
{
    public decimal InitialBalance { get; set; } = 1000m;
    public decimal UsdBalance { get; set; } = 1000m;
    public decimal BtcBalance { get; set; } = 0m;

    // Position tracking
    public bool IsOpen { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal EntryFee { get; set; }
    public DateTime? EntryTime { get; set; }

    // Fee / P&L accounting (all fees in USD, P/L net of fees)
    public decimal TotalFeesPaid { get; set; }
    public decimal RealizedPnl { get; set; }

    // Control + last strategy output (shown in the Algorithms view)
    public bool IsEnabled { get; set; } = true;
    public string? LastSignal { get; set; }
    public string? LastSignalReason { get; set; }
    public decimal? LastSignalPrice { get; set; }
    public DateTime? LastSignalTime { get; set; }
    public DateTime? LastCandleTime { get; set; }

    public List<PaperTrade> TradeHistory { get; set; } = new();
}
