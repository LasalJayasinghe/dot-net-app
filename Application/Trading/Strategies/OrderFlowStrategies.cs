using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Strategies;

/// <summary>
/// Order-book imbalance: a smoothed bid/ask volume imbalance (top 20 levels, ~1 update/s) is checked on each
/// 1m close. Buys sustained bid pressure in a short-term uptrend; fixed TP/SL sized above fees, 60-min time stop.
/// </summary>
public sealed class OrderBookImbalanceStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const decimal Threshold = 0.25m;
    private const decimal Smoothing = 2m / 61m;  // EMA over ~60 snapshots (~1 min)
    private const decimal TakeProfit = 0.008m, StopLoss = 0.005m;
    private static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxDepthAge = TimeSpan.FromSeconds(15);

    private readonly Ema _trend = new(20);
    private decimal _imbalance;
    private bool _hasDepth;
    private DateTime _lastDepth, _cooldownUntil;

    public override string Id => "orderbook-imbalance-btc";
    public override string Name => "Order-Book Imbalance BTC";
    public override string Description => "Buys when the top-20 order book has held 25%+ more bid than ask volume for about a minute while price is above EMA20 (1m). +0.8% target, −0.5% stop, 60-minute time stop.";
    public override string Category => "Order flow";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1m";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1m")], WarmupCandles = 60, Depth = [Sym] };

    public override void OnDepth(DepthSnapshot depth, StrategyContext ctx)
    {
        _imbalance = _hasDepth ? _imbalance + (depth.Imbalance - _imbalance) * Smoothing : depth.Imbalance;
        _hasDepth = true;
        _lastDepth = depth.Time;
    }

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _trend.Update(c.Close);
        if (ctx.IsWarmup || !_trend.IsReady) return;

        if (ApplyProtection(c, ctx)) { _cooldownUntil = ctx.Now + Cooldown; return; }

        if (ctx.Holding(Sym) is { } h)
        {
            var reason = ctx.Now - h.OpenedAt >= MaxHold ? "Time stop (60 min)"
                : _imbalance < -Threshold ? $"Order book flipped to sellers ({_imbalance:P0})"
                : null;
            if (reason is not null && ctx.SellAll(Sym, c.Close, reason) is not null)
                _cooldownUntil = ctx.Now + Cooldown;
            return;
        }

        var depthFresh = _hasDepth && DateTime.UtcNow - _lastDepth < MaxDepthAge;
        if (depthFresh && ctx.Now >= _cooldownUntil && _imbalance > Threshold && c.Close > _trend.Value)
        {
            ctx.BuyAll(Sym, c.Close, $"Bid-side imbalance {_imbalance:P0}",
                stopLoss: c.Close * (1 - StopLoss), takeProfit: c.Close * (1 + TakeProfit));
        }
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["Imbalance%"] = Round(_imbalance * 100, 1), ["EMA20"] = Round(_trend.Value, 2),
    };
}

/// <summary>
/// Large-trade following: tracks taker trades ≥ $100k over a rolling 5-minute window (O(1) per trade).
/// On each 1m close, buys when net whale buying is ≥ $1.5M and buyers outweigh sellers 2:1.
/// </summary>
public sealed class WhaleFollowStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const decimal WhaleSize = 100_000m;
    private const decimal NetThreshold = 1_500_000m;
    private const decimal Dominance = 2m;
    private const decimal TakeProfit = 0.01m, StopLoss = 0.006m;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    private readonly Queue<(DateTime Time, decimal Quote, bool Buy)> _whales = new();
    private decimal _buyVol, _sellVol;
    private DateTime _cooldownUntil;

    public override string Id => "whale-follow-btc";
    public override string Name => "Whale Follow BTC";
    public override string Description => "Follows large taker trades (≥$100k). Buys when net whale buying over 5 minutes is ≥$1.5M with a 2:1 buy/sell ratio. +1% target, −0.6% stop, 90-minute time stop.";
    public override string Category => "Order flow";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1m";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1m")], WarmupCandles = 5, AggTrades = [Sym] };

    public override void OnAggTrade(in AggTrade trade, StrategyContext ctx)
    {
        var quote = trade.QuoteValue;
        if (quote < WhaleSize) return; // cheap filter: the vast majority of trades stop here

        _whales.Enqueue((trade.Time, quote, trade.IsTakerBuy));
        if (trade.IsTakerBuy) _buyVol += quote; else _sellVol += quote;
        Evict(trade.Time);
    }

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        Evict(c.CloseTime);
        if (ctx.IsWarmup) return;

        if (ApplyProtection(c, ctx)) { _cooldownUntil = ctx.Now + Cooldown; return; }

        var net = _buyVol - _sellVol;

        if (ctx.Holding(Sym) is { } h)
        {
            var reason = ctx.Now - h.OpenedAt >= MaxHold ? "Time stop (90 min)"
                : net <= -NetThreshold && _sellVol >= Dominance * _buyVol ? $"Whales turned sellers (net {net / 1_000_000m:0.0}M)"
                : null;
            if (reason is not null && ctx.SellAll(Sym, c.Close, reason) is not null)
                _cooldownUntil = ctx.Now + Cooldown;
            return;
        }

        if (ctx.Now >= _cooldownUntil && net >= NetThreshold && _buyVol >= Dominance * _sellVol)
        {
            ctx.BuyAll(Sym, c.Close, $"Net whale buying ${net / 1_000_000m:0.0}M in 5 min",
                stopLoss: c.Close * (1 - StopLoss), takeProfit: c.Close * (1 + TakeProfit));
        }
    }

    private void Evict(DateTime now)
    {
        var cutoff = now - Window;
        while (_whales.Count > 0 && _whales.Peek().Time < cutoff)
        {
            var (_, quote, buy) = _whales.Dequeue();
            if (buy) _buyVol -= quote; else _sellVol -= quote;
        }
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["WhaleBuys5m$"] = Round(_buyVol, 0), ["WhaleSells5m$"] = Round(_sellVol, 0), ["WhaleTrades5m"] = _whales.Count,
    };
}

/// <summary>
/// Funding-rate contrarian (spot, long-only): when perpetual funding turns negative the crowd is short,
/// which often precedes squeezes. Buys spot; exits when funding gets crowded long, or on 2×ATR stop / 3×ATR target.
/// </summary>
public sealed class FundingContrarianStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const decimal EntryRate = -0.0001m;   // −0.01% per 8h
    private const decimal ExitRate = 0.0003m;     // +0.03% per 8h
    private static readonly TimeSpan MaxFundingAge = TimeSpan.FromMinutes(30);

    private readonly Atr _atr = new(14);
    private decimal? _rate;
    private DateTime _rateTime;

    public override string Id => "funding-contrarian-btc";
    public override string Name => "Funding Contrarian BTC";
    public override string Description => "Buys spot BTC when perpetual funding is ≤ −0.01% (shorts crowded); exits when funding is ≥ +0.03%, or at a 2×ATR stop / 3×ATR target.";
    public override string Category => "Order flow";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 50, Funding = [Sym] };

    public override void OnFunding(FundingRate funding, StrategyContext ctx)
    {
        _rate = funding.Rate;
        _rateTime = funding.Time;
    }

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _atr.Update(c.High, c.Low, c.Close);
        if (ctx.IsWarmup || !_atr.IsReady || _rate is not { } rate) return;
        if (ApplyProtection(c, ctx)) return;

        if (ctx.HasPosition(Sym))
        {
            if (rate >= ExitRate)
                ctx.SellAll(Sym, c.Close, $"Funding crowded long ({rate * 100:0.####}%)");
            return;
        }

        if (DateTime.UtcNow - _rateTime < MaxFundingAge && rate <= EntryRate)
        {
            var atr = _atr.Value;
            ctx.BuyAll(Sym, c.Close, $"Negative funding ({rate * 100:0.####}%) — shorts crowded",
                stopLoss: c.Close - 2 * atr, takeProfit: c.Close + 3 * atr);
        }
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["Funding%"] = Round((_rate ?? 0) * 100, 4), ["ATR14"] = Round(_atr.Value, 2),
    };
}
