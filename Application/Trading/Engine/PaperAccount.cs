namespace dotnetApp.Application.Trading.Engine;

public sealed class HoldingState
{
    public decimal Quantity { get; set; }

    /// <summary>Σ quantity × buy price for the quantity still held (excludes fees).</summary>
    public decimal GrossCost { get; set; }

    /// <summary>Binance BUY fees attributable to the quantity still held.</summary>
    public decimal EntryFees { get; set; }

    public DateTime OpenedAt { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }

    public decimal CostBasis => GrossCost + EntryFees;
    public decimal AvgEntryPrice => Quantity == 0 ? 0 : GrossCost / Quantity;
}

public sealed class TradeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = ""; // BUY / SELL
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal GrossValue { get; set; }
    public decimal Fee { get; set; }

    /// <summary>BUY: USD spent incl. fee. SELL: USD received after fee.</summary>
    public decimal NetValue { get; set; }

    public DateTime Timestamp { get; set; }
    public string Reason { get; set; } = "";

    // SELL only
    public decimal? EntryPrice { get; set; }
    public decimal? EntryFee { get; set; }
    public decimal? CostBasis { get; set; }
    public decimal? RealizedPnl { get; set; }
    public int? HoldMinutes { get; set; }
}

public sealed class SignalRecord
{
    public string Symbol { get; set; } = "";
    public string Type { get; set; } = ""; // BUY / SELL
    public decimal Price { get; set; }
    public string Reason { get; set; } = "";
    public DateTime Time { get; set; }
    public bool Executed { get; set; }
}

/// <summary>Everything persisted for one bot (PaperTrading/{id}.json).</summary>
public sealed class BotState
{
    public int Version { get; set; } = 1;
    public decimal InitialBalance { get; set; } = 1000m;
    public decimal Cash { get; set; } = 1000m;
    public decimal TotalFees { get; set; }
    public decimal RealizedPnl { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastEventTime { get; set; }
    public Dictionary<string, HoldingState> Holdings { get; set; } = new();
    public List<TradeRecord> Trades { get; set; } = new();
    public List<SignalRecord> Signals { get; set; } = new();

    /// <summary>Strategy-specific state (grid lots, DCA schedule …) serialized by the strategy.</summary>
    public string? StrategyState { get; set; }
}

/// <summary>
/// Simulated Binance spot account. Every executed side pays <see cref="FeeRate"/>;
/// realized P/L is net of BOTH the entry fee (carried in the cost basis) and the exit fee.
/// </summary>
public sealed class PaperAccount
{
    public const int MaxTrades = 1000;
    public const int MaxSignals = 50;
    public const decimal MinNotional = 5m;      // Binance spot minimum order value
    private const decimal QtyStep = 0.000001m;  // 6 dp quantity precision

    public PaperAccount(BotState state, decimal feeRate)
    {
        State = state;
        FeeRate = feeRate;
    }

    public BotState State { get; set; }
    public decimal FeeRate { get; }

    /// <summary>Buys with up to <paramref name="usdAmount"/> USD (fee included). Returns null if nothing was bought.</summary>
    public TradeRecord? Buy(string symbol, decimal price, decimal usdAmount, DateTime time, string reason)
    {
        if (price <= 0 || usdAmount <= 0) return null;

        var spend = Math.Min(usdAmount, State.Cash);
        var quantity = Math.Floor(spend / (1 + FeeRate) / price / QtyStep) * QtyStep;
        var gross = quantity * price;
        if (quantity <= 0 || gross < MinNotional) return null;

        var fee = gross * FeeRate;
        State.Cash -= gross + fee;
        State.TotalFees += fee;

        if (!State.Holdings.TryGetValue(symbol, out var holding))
        {
            holding = new HoldingState { OpenedAt = time };
            State.Holdings[symbol] = holding;
        }
        holding.Quantity += quantity;
        holding.GrossCost += gross;
        holding.EntryFees += fee;

        var trade = new TradeRecord
        {
            Symbol = symbol,
            Side = "BUY",
            Quantity = quantity,
            Price = price,
            GrossValue = gross,
            Fee = fee,
            NetValue = gross + fee,
            Timestamp = time,
            Reason = reason,
        };
        AddTrade(trade);
        return trade;
    }

    /// <summary>Sells using average-cost accounting.</summary>
    public TradeRecord? Sell(string symbol, decimal price, decimal quantity, DateTime time, string reason) =>
        SellCore(symbol, price, quantity, time, reason, lotGrossCost: null, lotEntryFee: null);

    /// <summary>Sells a specific lot (grid trading) so P/L reflects that lot's own buy price and fee.</summary>
    public TradeRecord? SellLot(string symbol, decimal price, decimal quantity, decimal lotGrossCost, decimal lotEntryFee,
        DateTime time, string reason) =>
        SellCore(symbol, price, quantity, time, reason, lotGrossCost, lotEntryFee);

    private TradeRecord? SellCore(string symbol, decimal price, decimal quantity, DateTime time, string reason,
        decimal? lotGrossCost, decimal? lotEntryFee)
    {
        if (price <= 0 || !State.Holdings.TryGetValue(symbol, out var holding) || holding.Quantity <= 0)
            return null;

        var qty = Math.Min(quantity, holding.Quantity);
        if (qty <= 0) return null;

        var portion = qty / holding.Quantity;
        var grossCostSold = Math.Min(lotGrossCost ?? holding.GrossCost * portion, holding.GrossCost);
        var entryFeeSold = Math.Min(lotEntryFee ?? holding.EntryFees * portion, holding.EntryFees);

        var gross = qty * price;
        var fee = gross * FeeRate;
        var net = gross - fee;
        var costBasis = grossCostSold + entryFeeSold;
        var pnl = net - costBasis;

        State.Cash += net;
        State.TotalFees += fee;
        State.RealizedPnl += pnl;
        if (pnl > 0) State.Wins++; else State.Losses++;

        var holdMinutes = (int)Math.Max(0, (time - holding.OpenedAt).TotalMinutes);

        holding.Quantity -= qty;
        holding.GrossCost -= grossCostSold;
        holding.EntryFees -= entryFeeSold;
        if (holding.Quantity <= QtyStep / 2)
            State.Holdings.Remove(symbol);

        var trade = new TradeRecord
        {
            Symbol = symbol,
            Side = "SELL",
            Quantity = qty,
            Price = price,
            GrossValue = gross,
            Fee = fee,
            NetValue = net,
            Timestamp = time,
            Reason = reason,
            EntryPrice = qty == 0 ? 0 : grossCostSold / qty,
            EntryFee = entryFeeSold,
            CostBasis = costBasis,
            RealizedPnl = pnl,
            HoldMinutes = holdMinutes,
        };
        AddTrade(trade);
        return trade;
    }

    public void AddSignal(SignalRecord signal)
    {
        State.Signals.Add(signal);
        if (State.Signals.Count > MaxSignals)
            State.Signals.RemoveRange(0, State.Signals.Count - MaxSignals);
    }

    private void AddTrade(TradeRecord trade)
    {
        State.Trades.Add(trade);
        if (State.Trades.Count > MaxTrades)
            State.Trades.RemoveRange(0, State.Trades.Count - MaxTrades);
    }
}
