using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Strategies;

/// <summary>Buy closes below the lower Bollinger band, take profit at the middle band, 2×ATR stop.</summary>
public sealed class BollingerReversionStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";

    /// <summary>Skip entries where the distance to the target can't comfortably cover round-trip fees.</summary>
    private const decimal MinEdge = 0.006m;

    private readonly Bollinger _bb = new(20, 2m);
    private readonly Atr _atr = new(14);

    public override string Id => "bb-reversion-btc-15m";
    public override string Name => "Bollinger Reversion BTC";
    public override string Description => "Buys closes below the lower Bollinger band (20, 2σ) if the middle band is ≥0.6% away; exits at the middle band or a 2×ATR stop.";
    public override string Category => "Mean reversion";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "15m";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "15m")], WarmupCandles = 100 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _bb.Update(c.Close);
        _atr.Update(c.High, c.Low, c.Close);
        if (ctx.IsWarmup || !_bb.IsReady || !_atr.IsReady) return;
        if (ApplyProtection(c, ctx)) return;

        if (ctx.HasPosition(Sym))
        {
            if (c.Close >= _bb.Middle)
                ctx.SellAll(Sym, c.Close, "Reverted to middle band");
            return;
        }

        if (c.Close < _bb.Lower && (_bb.Middle - c.Close) / c.Close >= MinEdge)
            ctx.BuyAll(Sym, c.Close, "Close below lower Bollinger band", stopLoss: c.Close - 2 * _atr.Value);
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["Upper"] = Round(_bb.Upper, 2), ["Middle"] = Round(_bb.Middle, 2), ["Lower"] = Round(_bb.Lower, 2),
        ["ATR14"] = Round(_atr.Value, 2),
    };
}

/// <summary>Buy RSI(14) &lt; 30, sell RSI &gt; 55, 2×ATR stop.</summary>
public sealed class RsiReversionStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private readonly Rsi _rsi = new(14);
    private readonly Atr _atr = new(14);

    public override string Id => "rsi-reversion-btc-15m";
    public override string Name => "RSI Reversion BTC";
    public override string Description => "Buys when RSI(14) drops below 30; sells when it recovers above 55 or a 2×ATR stop is hit.";
    public override string Category => "Mean reversion";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "15m";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "15m")], WarmupCandles = 150 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _rsi.Update(c.Close);
        _atr.Update(c.High, c.Low, c.Close);
        if (ctx.IsWarmup || !_rsi.IsReady || !_atr.IsReady) return;
        if (ApplyProtection(c, ctx)) return;

        if (ctx.HasPosition(Sym))
        {
            if (_rsi.Value > 55)
                ctx.SellAll(Sym, c.Close, $"RSI recovered to {_rsi.Value:0.0}");
            return;
        }

        if (_rsi.Value < 30)
            ctx.BuyAll(Sym, c.Close, $"RSI oversold at {_rsi.Value:0.0}", stopLoss: c.Close - 2 * _atr.Value);
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["RSI14"] = Round(_rsi.Value, 2), ["ATR14"] = Round(_atr.Value, 2),
    };
}

/// <summary>Buy ≥1.5% below the rolling 24h VWAP, exit at VWAP, 2×ATR stop.</summary>
public sealed class VwapReversionStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const decimal EntryDiscount = 0.015m;

    private readonly RollingVwap _vwap = new(24);
    private readonly Atr _atr = new(14);

    public override string Id => "vwap-reversion-btc-1h";
    public override string Name => "VWAP Reversion BTC";
    public override string Description => "Buys when price is 1.5%+ below the rolling 24-hour VWAP; exits at VWAP or a 2×ATR stop.";
    public override string Category => "Mean reversion";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 100 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _vwap.Update(c.High, c.Low, c.Close, c.Volume);
        _atr.Update(c.High, c.Low, c.Close);
        if (ctx.IsWarmup || !_vwap.IsReady || !_atr.IsReady) return;
        if (ApplyProtection(c, ctx)) return;

        var vwap = _vwap.Value;
        if (ctx.HasPosition(Sym))
        {
            if (c.Close >= vwap)
                ctx.SellAll(Sym, c.Close, "Reverted to VWAP");
            return;
        }

        if (c.Close <= vwap * (1 - EntryDiscount))
            ctx.BuyAll(Sym, c.Close, $"{(1 - c.Close / vwap) * 100:0.00}% below 24h VWAP", stopLoss: c.Close - 2 * _atr.Value);
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["VWAP24"] = Round(_vwap.Value, 2), ["ATR14"] = Round(_atr.Value, 2),
    };
}

/// <summary>Bollinger squeeze: after bandwidth hits a 120-bar low, buy an upside break of the upper band.</summary>
public sealed class BollingerSqueezeStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const int ArmedBars = 6;

    private readonly Bollinger _bb = new(20, 2m);
    private readonly RollingExtreme _minBandwidth = RollingExtreme.Min(120);
    private readonly Atr _atr = new(14);
    private int _armedFor;

    public override string Id => "bb-squeeze-btc-1h";
    public override string Name => "Bollinger Squeeze BTC";
    public override string Description => "When band width hits a 120-hour low, buys the next close above the upper band; 2×ATR stop, 4×ATR target, exits below the middle band.";
    public override string Category => "Volatility breakout";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 300 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _bb.Update(c.Close);
        _atr.Update(c.High, c.Low, c.Close);
        if (!_bb.IsReady) return;

        var bw = _bb.Bandwidth;
        _minBandwidth.Update(bw);
        if (_minBandwidth.IsReady && bw <= _minBandwidth.Value * 1.05m) _armedFor = ArmedBars;
        else if (_armedFor > 0) _armedFor--;

        if (ctx.IsWarmup || !_minBandwidth.IsReady || !_atr.IsReady) return;
        if (ApplyProtection(c, ctx)) return;

        if (ctx.HasPosition(Sym))
        {
            if (c.Close < _bb.Middle)
                ctx.SellAll(Sym, c.Close, "Breakout failed (below middle band)");
            return;
        }

        if (_armedFor > 0 && c.Close > _bb.Upper)
        {
            var atr = _atr.Value;
            ctx.BuyAll(Sym, c.Close, "Squeeze breakout above upper band",
                stopLoss: c.Close - 2 * atr, takeProfit: c.Close + 4 * atr);
        }
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["Bandwidth%"] = Round(_bb.Bandwidth * 100, 3), ["MinBandwidth%"] = Round(_minBandwidth.Value * 100, 3),
        ["Upper"] = Round(_bb.Upper, 2), ["Middle"] = Round(_bb.Middle, 2), ["SqueezeArmed"] = _armedFor > 0 ? 1 : 0,
    };
}
