using System.Text.Json;
using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Strategies;

/// <summary>Dollar-cost averaging: buy $25 of BTC every 24h, $50 when price is 5%+ below its 200-hour average. Never sells.</summary>
public sealed class DcaStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const decimal BaseAmount = 25m;
    private const decimal DipAmount = 50m;
    private const decimal DipThreshold = 0.05m;
    private static readonly TimeSpan Every = TimeSpan.FromHours(24);

    private readonly Sma _sma = new(200);
    private DateTime? _lastBuy;

    public override string Id => "dca-btc";
    public override string Name => "DCA BTC";
    public override string Description => "Buys $25 of BTC every 24 hours, or $50 when price is 5%+ below its 200-hour average. Accumulates; never sells.";
    public override string Category => "Systematic";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1h";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1h")], WarmupCandles = 220 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        _sma.Update(c.Close);
        if (ctx.IsWarmup) return;
        if (_lastBuy is { } last && c.CloseTime - last < Every) return;
        if (ctx.Cash < PaperAccount.MinNotional) return;

        var dip = _sma.IsReady && c.Close < _sma.Value * (1 - DipThreshold);
        var amount = dip ? DipAmount : BaseAmount;
        var reason = dip ? $"Scheduled buy ×2 (price {(1 - c.Close / _sma.Value) * 100:0.0}% below 200h avg)" : "Scheduled daily buy";

        if (ctx.Buy(Sym, c.Close, amount, reason) is not null)
        {
            _lastBuy = c.CloseTime;
            ctx.MarkStateChanged();
        }
    }

    public override IReadOnlyDictionary<string, decimal> Indicators() => new Dictionary<string, decimal>
    {
        ["SMA200h"] = Round(_sma.Value, 2),
        ["HoursToNextBuy"] = _lastBuy is { } l ? Round((decimal)Math.Max(0, (l + Every - DateTime.UtcNow).TotalHours), 1) : 0,
    };

    public override string? SaveState() => JsonSerializer.Serialize(new State(_lastBuy));
    public override void LoadState(string json) => _lastBuy = JsonSerializer.Deserialize<State>(json)?.LastBuy;

    private sealed record State(DateTime? LastBuy);
}

/// <summary>
/// Long-only grid: 10 buy levels spaced 1% apart below an anchor price, $100 each.
/// Each filled lot sells one level (1%) higher, so every round trip nets ~0.8% after fees.
/// Re-anchors upward when price leaves the grid while flat.
/// </summary>
public sealed class GridStrategy : TradingStrategyBase
{
    private const string Sym = "BTCUSDT";
    private const int Levels = 10;
    private const decimal Spacing = 0.01m;

    private decimal? _anchor;
    private List<GridLot> _lots = new();

    public override string Id => "grid-btc";
    public override string Name => "Grid BTC";
    public override string Description => "10 buy levels 1% apart below the anchor price ($100 each). Each filled level sells 1% higher, about 0.8% profit per round trip after the 0.2% fees.";
    public override string Category => "Systematic";
    public override string Pair => "BTC/USDT";
    public override string Timeframe => "1m";
    public override StrategyFeeds Feeds { get; } = new() { Candles = [new(Sym, "1m")], WarmupCandles = 5 };

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        if (ctx.IsWarmup) return;

        if (_anchor is null)
        {
            _anchor = c.Close;
            ctx.MarkStateChanged();
            return;
        }

        // 1) Sell lots filled on earlier candles whose target was reached.
        //    Levels sold here are not re-bought in the same candle (intra-candle order is unknown).
        var soldLevels = 0;
        for (var i = _lots.Count - 1; i >= 0; i--)
        {
            var lot = _lots[i];
            var target = lot.LevelPrice * (1 + Spacing);
            if (c.High < target) continue;

            if (ctx.SellLot(Sym, Math.Max(c.Open, target), lot.Quantity, lot.GrossCost, lot.Fee,
                    $"Grid level {lot.Level} take-profit") is not null)
            {
                _lots.RemoveAt(i);
                soldLevels |= 1 << lot.Level;
                ctx.MarkStateChanged();
            }
        }

        // 2) Buy any empty level the candle traded down to.
        var perLevel = ctx.InitialBalance / Levels;
        for (var level = 1; level <= Levels; level++)
        {
            if ((soldLevels & (1 << level)) != 0 || _lots.Exists(l => l.Level == level)) continue;
            var price = LevelPrice(level);
            if (c.Low > price) continue;

            var trade = ctx.Buy(Sym, Math.Min(c.Open, price), perLevel, $"Grid level {level} buy");
            if (trade is null) continue;
            _lots.Add(new GridLot(level, price, trade.Quantity, trade.GrossValue, trade.Fee));
            ctx.MarkStateChanged();
        }

        // 3) Follow the market up when flat and above the grid.
        if (_lots.Count == 0 && c.Close > _anchor.Value * (1 + Spacing))
        {
            _anchor = c.Close;
            ctx.MarkStateChanged();
        }
    }

    private decimal LevelPrice(int level)
    {
        var p = _anchor!.Value;
        for (var i = 0; i < level; i++) p *= 1 - Spacing;
        return Math.Round(p, 2);
    }

    public override IReadOnlyDictionary<string, decimal> Indicators()
    {
        var d = new Dictionary<string, decimal> { ["Anchor"] = Round(_anchor ?? 0, 2), ["OpenLots"] = _lots.Count };
        if (_anchor is not null)
        {
            d["NextBuyLevel"] = LevelPrice(Enumerable.Range(1, Levels).FirstOrDefault(l => !_lots.Exists(x => x.Level == l), Levels));
            d["LowestLevel"] = LevelPrice(Levels);
        }
        return d;
    }

    public override string? SaveState() => JsonSerializer.Serialize(new State(_anchor, _lots));

    public override void LoadState(string json)
    {
        var s = JsonSerializer.Deserialize<State>(json);
        _anchor = s?.Anchor;
        _lots = s?.Lots ?? new List<GridLot>();
    }

    private sealed record GridLot(int Level, decimal LevelPrice, decimal Quantity, decimal GrossCost, decimal Fee);
    private sealed record State(decimal? Anchor, List<GridLot> Lots);
}
