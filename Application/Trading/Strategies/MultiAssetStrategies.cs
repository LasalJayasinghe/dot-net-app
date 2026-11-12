using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Strategies;

/// <summary>
/// Every 4h, hold the coin with the strongest 24h momentum (cash if none is positive).
/// Switches only when the new leader beats the current holding by a margin, to limit fee churn.
/// </summary>
public sealed class MomentumRotationStrategy : TradingStrategyBase
{
    private static readonly string[] Universe = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT", "DOGEUSDT"];
    private const int Lookback = 24;          // hours
    private const int RebalanceEveryHours = 4;
    private const decimal SwitchMargin = 0.01m;

    private readonly Dictionary<string, RateOfChange> _roc = Universe.ToDictionary(s => s, _ => new RateOfChange(Lookback));
    private readonly Dictionary<string, DateTime> _lastOpen = new();
    private readonly Dictionary<string, decimal> _lastClose = new();
    private DateTime _lastEvaluated;

    public override string Id => "momentum-rotation-1h";
    public override string Name => "Momentum Rotation";
    public override string Description => "Every 4 hours holds whichever of BTC/ETH/BNB/SOL/XRP/ADA/DOGE has the best 24h return (cash if none is positive). Switches only if the new leader is 1%+ ahead.";
    public override string Category => "Multi-asset";
    public override string Pair => "7 coins";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new()
    {
        Candles = Universe.Select(s => new CandleFeed(s, "1h")).ToArray(),
        WarmupCandles = 50,
    };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _roc[c.Symbol].Update(c.Close);
        _lastOpen[c.Symbol] = c.OpenTime;
        _lastClose[c.Symbol] = c.Close;

        // Wait until every coin has closed this same hour, then evaluate once.
        if (c.OpenTime == _lastEvaluated || _lastOpen.Count < Universe.Length) return;
        foreach (var s in Universe)
            if (_lastOpen[s] != c.OpenTime) return;
        _lastEvaluated = c.OpenTime;

        if (ctx.IsWarmup || (c.OpenTime.Hour + 1) % RebalanceEveryHours != 0) return;
        if (_roc.Values.Any(r => !r.IsReady)) return;

        var leader = Universe.MaxBy(s => _roc[s].Value)!;
        var leaderRoc = _roc[leader].Value;
        var held = ctx.HeldSymbol();

        if (leaderRoc <= 0)
        {
            if (held is not null)
                ctx.SellAll(held, _lastClose[held], "No coin has positive 24h momentum → cash");
            return;
        }

        if (held is null)
        {
            ctx.BuyAll(leader, _lastClose[leader], $"Strongest 24h momentum (+{leaderRoc * 100:0.00}%)");
            return;
        }

        if (held != leader && leaderRoc - _roc[held].Value > SwitchMargin)
        {
            if (ctx.SellAll(held, _lastClose[held], $"Rotating into {Symbols.ToPair(leader)}") is not null)
                ctx.BuyAll(leader, _lastClose[leader], $"Strongest 24h momentum (+{leaderRoc * 100:0.00}%)");
        }
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() =>
        Universe.ToDictionary(s => $"{s[..^4]} 24h%", s => Round(_roc[s].Value * 100, 2));
}

/// <summary>
/// ETH/BTC ratio mean reversion, long-only: buy the leg that is ≥2σ cheap relative to the other,
/// exit when the ratio returns to its 100-hour mean. 5% catastrophic stop.
/// </summary>
public sealed class EthBtcPairsStrategy : TradingStrategyBase
{
    private const string Eth = "ETHUSDT", Btc = "BTCUSDT";
    private const decimal EntryZ = 2m;

    private readonly RollingStats _ratio = new(100);
    private DateTime _ethOpen, _btcOpen, _lastEvaluated;
    private decimal _eth, _btc, _z;

    public override string Id => "eth-btc-pairs-1h";
    public override string Name => "ETH/BTC Pairs";
    public override string Description => "Tracks the ETH/BTC price ratio. Buys ETH when it's 2σ cheap vs BTC (or BTC when it's 2σ cheap vs ETH); exits when the ratio returns to its 100-hour mean.";
    public override string Category => "Multi-asset";
    public override string Pair => "ETH/BTC";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new()
    {
        Candles = [new(Eth, "1h"), new(Btc, "1h")],
        WarmupCandles = 200,
    };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        if (c.Symbol == Eth) { _eth = c.Close; _ethOpen = c.OpenTime; }
        else { _btc = c.Close; _btcOpen = c.OpenTime; }

        if (!ctx.IsWarmup && ApplyProtection(c, ctx)) return;

        if (_ethOpen != _btcOpen || _ethOpen == _lastEvaluated || _btc == 0) return;
        _lastEvaluated = _ethOpen;

        var ratio = _eth / _btc;
        _ratio.Update(ratio);
        if (!_ratio.IsReady) return;
        _z = _ratio.ZScore(ratio);
        if (ctx.IsWarmup) return;

        if (ctx.HasPosition(Eth))
        {
            if (_z >= 0) ctx.SellAll(Eth, _eth, $"ETH/BTC ratio back to mean (z={_z:0.00})");
            return;
        }
        if (ctx.HasPosition(Btc))
        {
            if (_z <= 0) ctx.SellAll(Btc, _btc, $"ETH/BTC ratio back to mean (z={_z:0.00})");
            return;
        }

        if (_z <= -EntryZ)
            ctx.BuyAll(Eth, _eth, $"ETH cheap vs BTC (z={_z:0.00})", stopLoss: _eth * 0.95m);
        else if (_z >= EntryZ)
            ctx.BuyAll(Btc, _btc, $"BTC cheap vs ETH (z={_z:0.00})", stopLoss: _btc * 0.95m);
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["ETH/BTC"] = _btc == 0 ? 0 : Round(_eth / _btc, 6), ["Mean"] = Round(_ratio.Mean, 6), ["Z"] = Round(_z, 2),
    };
}
