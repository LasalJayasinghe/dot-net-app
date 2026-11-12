using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Strategies;

/// <summary>The original bot: EMA 9/21 + RSI(14) on BTCUSDT 5m. Same entry/exit rules, streaming indicators.</summary>
public sealed class EmaRsiStrategy : TradingStrategyBase
{
    public const string StrategyId = "ema-rsi-btc";
    private const string Sym = "BTCUSDT";

    private readonly Ema _fast = new(9), _slow = new(21);
    private readonly Rsi _rsi = new(14);

    public override string Id => StrategyId;
    public override string Name => "EMA/RSI BTC";
    public override string Description => "Buys when EMA9 > EMA21 and RSI(14) < 30; sells on a bearish EMA crossover.";
    public override string Category => "Trend";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "5m";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "5m")], WarmupCandles = 200 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _fast.Update(c.Close);
        _slow.Update(c.Close);
        _rsi.Update(c.Close);
        if (ctx.IsWarmup || !_slow.IsReady || !_rsi.IsReady) return;

        if (!ctx.HasPosition(Sym) && _fast.Value > _slow.Value && _rsi.Value < 30)
            ctx.BuyAll(Sym, c.Close, "EMA9 > EMA21 + RSI oversold");
        else if (ctx.HasPosition(Sym) && _fast.Value < _slow.Value)
            ctx.SellAll(Sym, c.Close, "EMA bearish crossover");
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["EMA9"] = Round(_fast.Value, 2), ["EMA21"] = Round(_slow.Value, 2), ["RSI14"] = Round(_rsi.Value, 2),
    };
}

/// <summary>MACD(12,26,9) histogram crossover on BTCUSDT 1h.</summary>
public sealed class MacdStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private readonly Macd _macd = new();
    private decimal? _prevHist;

    public override string Id => "macd-btc-1h";
    public override string Name => "MACD Crossover BTC";
    public override string Description => "Buys when the MACD line crosses above its signal line; sells on the opposite cross.";
    public override string Category => "Trend";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 300 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _macd.Update(c.Close);
        if (!_macd.IsReady) return;

        var hist = _macd.Histogram;
        var prev = _prevHist;
        _prevHist = hist;
        if (ctx.IsWarmup || prev is null) return;

        if (prev <= 0 && hist > 0 && !ctx.HasPosition(Sym))
            ctx.BuyAll(Sym, c.Close, "MACD bullish crossover");
        else if (prev >= 0 && hist < 0 && ctx.HasPosition(Sym))
            ctx.SellAll(Sym, c.Close, "MACD bearish crossover");
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["MACD"] = Round(_macd.Line, 2), ["Signal"] = Round(_macd.Signal, 2), ["Histogram"] = Round(_macd.Histogram, 2),
    };
}

/// <summary>Turtle-style Donchian breakout: enter above the 20-bar high, exit below the 10-bar low.</summary>
public sealed class DonchianBreakoutStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private readonly RollingExtreme _entryHigh = RollingExtreme.Max(20);
    private readonly RollingExtreme _exitLow = RollingExtreme.Min(10);

    public override string Id => "donchian-btc-1h";
    public override string Name => "Donchian Breakout BTC";
    public override string Description => "Buys when price closes above the prior 20-hour high; exits below the prior 10-hour low.";
    public override string Category => "Trend";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 50 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        // Compare against the channel of PRIOR candles, then include this one.
        var ready = _entryHigh.IsReady && _exitLow.IsReady;
        var high = _entryHigh.Value;
        var low = _exitLow.Value;
        _entryHigh.Update(c.High);
        _exitLow.Update(c.Low);
        if (ctx.IsWarmup || !ready) return;

        if (!ctx.HasPosition(Sym) && c.Close > high)
            ctx.BuyAll(Sym, c.Close, $"Breakout above 20h high {high:0.##}");
        else if (ctx.HasPosition(Sym) && c.Close < low)
            ctx.SellAll(Sym, c.Close, $"Close below 10h low {low:0.##}");
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["High20"] = Round(_entryHigh.Value, 2), ["Low10"] = Round(_exitLow.Value, 2),
    };
}

/// <summary>Supertrend(10, 3) direction flips on BTCUSDT 1h.</summary>
public sealed class SupertrendStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private readonly Supertrend _st = new(10, 3m);
    private int? _prevDir;

    public override string Id => "supertrend-btc-1h";
    public override string Name => "Supertrend BTC";
    public override string Description => "Long while Supertrend(10, 3×ATR) points up; exits when it flips down.";
    public override string Category => "Trend";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 200 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _st.Update(c.High, c.Low, c.Close);
        if (!_st.IsReady) return;

        var dir = _st.Direction;
        var prev = _prevDir;
        _prevDir = dir;
        if (ctx.IsWarmup || prev is null) return;

        if (prev < 0 && dir > 0 && !ctx.HasPosition(Sym))
            ctx.BuyAll(Sym, c.Close, "Supertrend flipped up");
        else if (prev > 0 && dir < 0 && ctx.HasPosition(Sym))
            ctx.SellAll(Sym, c.Close, "Supertrend flipped down");
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["Supertrend"] = Round(_st.Line, 2), ["Direction"] = _st.Direction,
    };
}

/// <summary>EMA 9/21 crossover on 1h candles — the slower, fee-friendlier version of the original idea.</summary>
public sealed class EmaCrossStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private readonly Ema _fast = new(9), _slow = new(21);
    private int _prevSign;

    public override string Id => "ema-cross-btc-1h";
    public override string Name => "EMA Crossover BTC";
    public override string Description => "Buys when EMA9 crosses above EMA21 on 1h candles; sells on the cross back down.";
    public override string Category => "Trend";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 200 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _fast.Update(c.Close);
        _slow.Update(c.Close);
        if (!_slow.IsReady) return;

        var sign = Math.Sign(_fast.Value - _slow.Value);
        var prev = _prevSign;
        _prevSign = sign;
        if (ctx.IsWarmup || prev == 0) return;

        if (prev < 0 && sign > 0 && !ctx.HasPosition(Sym))
            ctx.BuyAll(Sym, c.Close, "EMA9 crossed above EMA21");
        else if (prev > 0 && sign < 0 && ctx.HasPosition(Sym))
            ctx.SellAll(Sym, c.Close, "EMA9 crossed below EMA21");
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["EMA9"] = Round(_fast.Value, 2), ["EMA21"] = Round(_slow.Value, 2),
    };
}
