using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Engine;

public interface ITradingStrategy
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Category { get; }
    string Pair { get; }
    string Timeframe { get; }
    StrategyFeeds Feeds { get; }

    void OnCandle(Candle candle, string interval, StrategyContext ctx);
    void OnDepth(DepthSnapshot depth, StrategyContext ctx);
    void OnAggTrade(in AggTrade trade, StrategyContext ctx);
    void OnFunding(FundingRate funding, StrategyContext ctx);

    /// <summary>Current indicator values for the Algorithms view.</summary>
    IReadOnlyDictionary<string, decimal> Indicators();

    string? SaveState();
    void LoadState(string json);
}

/// <summary>Base class with no-op handlers and shared risk helpers.</summary>
public abstract class TradingStrategyBase : ITradingStrategy
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Category { get; }
    public abstract string Pair { get; }
    public abstract string Timeframe { get; }
    public abstract StrategyFeeds Feeds { get; }

    public abstract void OnCandle(Candle candle, string interval, StrategyContext ctx);
    public virtual void OnDepth(DepthSnapshot depth, StrategyContext ctx) { }
    public virtual void OnAggTrade(in AggTrade trade, StrategyContext ctx) { }
    public virtual void OnFunding(FundingRate funding, StrategyContext ctx) { }
    public virtual IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>();
    public virtual string? SaveState() => null;
    public virtual void LoadState(string json) { }

    /// <summary>
    /// Checks the open position's stop-loss / take-profit against this candle's range.
    /// Stop is checked first (conservative). Gaps fill at the open. Returns true if it exited.
    /// </summary>
    protected static bool ApplyProtection(Candle c, StrategyContext ctx)
    {
        var h = ctx.Holding(c.Symbol);
        if (h is null) return false;

        if (h.StopLoss is { } sl && c.Low <= sl)
            return ctx.SellAll(c.Symbol, Math.Min(c.Open, sl), $"Stop-loss hit ({sl:0.####})") is not null;

        if (h.TakeProfit is { } tp && c.High >= tp)
            return ctx.SellAll(c.Symbol, Math.Max(c.Open, tp), $"Take-profit hit ({tp:0.####})") is not null;

        return false;
    }

    protected static decimal Round(decimal v, int dp = 4) => Math.Round(v, dp);
}

/// <summary>
/// The only way a strategy touches its account. Orders are ignored during warm-up and
/// while the bot is stopped (signals are still recorded so the view shows what it would do).
/// </summary>
public sealed class StrategyContext
{
    private readonly PaperAccount _account;

    internal StrategyContext(PaperAccount account) => _account = account;

    public bool IsWarmup { get; internal set; }
    public bool IsLive => !IsWarmup;
    public DateTime Now { get; internal set; }
    internal bool Dirty { get; set; }

    public bool IsEnabled => _account.State.IsEnabled;
    public decimal Cash => _account.State.Cash;
    public decimal InitialBalance => _account.State.InitialBalance;
    public decimal FeeRate => _account.FeeRate;
    public bool IsFlat => _account.State.Holdings.Count == 0;

    public HoldingState? Holding(string symbol) =>
        _account.State.Holdings.TryGetValue(symbol, out var h) && h.Quantity > 0 ? h : null;

    public bool HasPosition(string symbol) => Holding(symbol) is not null;

    /// <summary>First held symbol, if any (single-position multi-asset strategies).</summary>
    public string? HeldSymbol()
    {
        foreach (var kv in _account.State.Holdings)
            if (kv.Value.Quantity > 0) return kv.Key;
        return null;
    }

    public TradeRecord? Buy(string symbol, decimal price, decimal usdAmount, string reason,
        decimal? stopLoss = null, decimal? takeProfit = null)
    {
        if (IsWarmup) return null;

        TradeRecord? trade = null;
        if (IsEnabled)
        {
            trade = _account.Buy(symbol, price, usdAmount, Now, reason);
            if (trade is not null && Holding(symbol) is { } h)
            {
                if (stopLoss is not null) h.StopLoss = stopLoss;
                if (takeProfit is not null) h.TakeProfit = takeProfit;
            }
        }

        RecordSignal(symbol, "BUY", price, reason, trade is not null);
        return trade;
    }

    public TradeRecord? BuyAll(string symbol, decimal price, string reason,
        decimal? stopLoss = null, decimal? takeProfit = null) =>
        Buy(symbol, price, Cash, reason, stopLoss, takeProfit);

    public TradeRecord? SellAll(string symbol, decimal price, string reason) =>
        Holding(symbol) is { } h ? Sell(symbol, price, h.Quantity, reason) : null;

    public TradeRecord? Sell(string symbol, decimal price, decimal quantity, string reason)
    {
        if (IsWarmup) return null;
        var trade = IsEnabled ? _account.Sell(symbol, price, quantity, Now, reason) : null;
        RecordSignal(symbol, "SELL", price, reason, trade is not null);
        return trade;
    }

    public TradeRecord? SellLot(string symbol, decimal price, decimal quantity, decimal lotGrossCost, decimal lotEntryFee,
        string reason)
    {
        if (IsWarmup) return null;
        var trade = IsEnabled ? _account.SellLot(symbol, price, quantity, lotGrossCost, lotEntryFee, Now, reason) : null;
        RecordSignal(symbol, "SELL", price, reason, trade is not null);
        return trade;
    }

    public void SetProtection(string symbol, decimal? stopLoss, decimal? takeProfit)
    {
        if (IsWarmup || Holding(symbol) is not { } h) return;
        h.StopLoss = stopLoss;
        h.TakeProfit = takeProfit;
        Dirty = true;
    }

    /// <summary>Call when strategy-specific persistent state changed.</summary>
    public void MarkStateChanged() => Dirty = true;

    private void RecordSignal(string symbol, string type, decimal price, string reason, bool executed)
    {
        var signals = _account.State.Signals;
        var last = signals.Count > 0 ? signals[^1] : null;

        // Collapse repeats (e.g. a stopped bot re-emitting BUY every candle) unless a trade happened.
        if (!executed && last is not null && last.Symbol == symbol && last.Type == type)
            return;

        _account.AddSignal(new SignalRecord
        {
            Symbol = symbol,
            Type = type,
            Price = price,
            Reason = reason,
            Time = Now,
            Executed = executed,
        });
        Dirty = true;
    }
}
