using System.Text.Json;
using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Indicators;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Strategies;

/// <summary>Settings for <see cref="TrendRotationStrategy"/>. Defaults are the backtested "A9" rules.</summary>
public sealed record TrendRotationSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string[] Coins { get; init; } = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT", "DOGEUSDT"];
    public string Interval { get; init; } = "4h";

    /// <summary>Momentum = average over these windows of (return ÷ volatility over the window).</summary>
    public int[] LookbackDays { get; init; } = [7, 14, 30];
    public int VolatilityDays { get; init; } = 30;

    /// <summary>Number of coins held.</summary>
    public int TopK { get; init; } = 2;

    /// <summary>Decision cadence; decisions happen on bars closing at a multiple of this (UTC), i.e. 00:00 UTC daily.</summary>
    public int RebalanceHours { get; init; } = 24;

    /// <summary>Risk-on only while BTC closes above its N-day average; otherwise everything goes to cash.</summary>
    public int RegimeDays { get; init; } = 100;

    /// <summary>A coin is eligible only while above its own N-day average.</summary>
    public int CoinTrendDays { get; init; } = 50;

    /// <summary>Per-coin trailing stop at N × ATR(14); 0 = no stop.</summary>
    public decimal AtrStop { get; init; } = 3;

    /// <summary>Hysteresis: keep a held coin while its rank ≤ TopK + RankBuffer, or for at least MinHoldHours.</summary>
    public int RankBuffer { get; init; } = 1;
    public int MinHoldHours { get; init; } = 48;

    /// <summary>Re-weight a held coin only if it is more than this far off its target size (fee control).</summary>
    public decimal DriftBand { get; init; } = 0.25m;

    /// <summary>"A9" rules: top-2, 3×ATR trailing stop. Backtest 2021–Nov 2026 (fair coin list): +527% vs BTC +121%, worst drop 48%.</summary>
    public static TrendRotationSettings Balanced => new() { Id = "trend-rotation-6", Name = "Trend Rotation 6" };

    /// <summary>Calmer variant picked by both walk-forward tuning rounds: top-3, no trailing stop.</summary>
    public static TrendRotationSettings Calm => new()
    {
        Id = "trend-rotation-6-calm", Name = "Trend Rotation 6 (Calm)", TopK = 3, AtrStop = 0,
    };
}

/// <summary>
/// Regime-filtered, risk-adjusted momentum rotation across 6 coins.
/// Once a day: if BTC is above its 100-day average, hold the top coins (above their own 50-day average, positive
/// momentum) weighted by inverse volatility; otherwise hold cash. Hysteresis, a drift band and a 48h minimum hold
/// keep turnover (fees) low. See mark document/Crypto Algo Research/Findings.md for the research behind it.
/// </summary>
public sealed class TrendRotationStrategy : TradingStrategyBase
{
    private const string Btc = "BTCUSDT";

    private readonly TrendRotationSettings _p;
    private readonly int _barsPerDay;
    private readonly int _maxBars;
    private readonly Dictionary<string, List<decimal>> _closes = new();
    private readonly Dictionary<string, Atr> _atr = new();
    private readonly Dictionary<string, DateTime> _open = new();
    private readonly Dictionary<string, decimal> _close = new();
    private readonly Dictionary<string, decimal> _scores = new();
    private Dictionary<string, DateTime> _heldSince = new();
    private Dictionary<string, DateTime> _cooldown = new();
    private DateTime _lastEval;
    private bool _riskOn;

    public TrendRotationStrategy(TrendRotationSettings settings)
    {
        _p = settings;
        _barsPerDay = (int)(24 / Intervals.ToTimeSpan(settings.Interval).TotalHours);
        _maxBars = (Math.Max(settings.RegimeDays, Math.Max(settings.CoinTrendDays,
            Math.Max(settings.VolatilityDays, settings.LookbackDays.Max()))) + 2) * _barsPerDay;
        foreach (var c in settings.Coins)
        {
            _closes[c] = new List<decimal>(_maxBars * 2);
            _atr[c] = new Atr(14);
        }
        if (!settings.Coins.Contains(Btc)) throw new ArgumentException("The coin list must include BTCUSDT (regime filter).");

        Feeds = new StrategyFeeds
        {
            Candles = settings.Coins.Select(c => new CandleFeed(c, settings.Interval)).ToArray(),
            WarmupCandles = 1000, // 4h × 1000 ≈ 166 days ≥ the 100-day regime average
        };
    }

    public override string Id => _p.Id;
    public override string Name => _p.Name;
    public override string Description =>
        $"Once a day, if BTC is above its {_p.RegimeDays}-day average, holds the top {_p.TopK} of " +
        $"{string.Join("/", _p.Coins.Select(c => c[..^4]))} by risk-adjusted {string.Join("/", _p.LookbackDays)}-day momentum " +
        $"(only coins above their {_p.CoinTrendDays}-day average), weighted by inverse volatility; otherwise cash." +
        (_p.AtrStop > 0 ? $" {_p.AtrStop:0.#}×ATR trailing stop per coin." : " No trailing stop.");
    public override string Category => "Multi-asset";
    public override string Pair => $"{_p.Coins.Length} coins";
    public override string Timeframe => _p.Interval;
    public override StrategyFeeds Feeds { get; }

    public override void OnCandle(Candle c, string interval, StrategyContext ctx)
    {
        var closes = _closes[c.Symbol];
        closes.Add(c.Close);
        if (closes.Count > _maxBars * 2) closes.RemoveRange(0, closes.Count - _maxBars);
        _atr[c.Symbol].Update(c.High, c.Low, c.Close);
        _open[c.Symbol] = c.OpenTime;
        _close[c.Symbol] = c.Close;

        if (!ctx.IsWarmup) TrailStop(c, ctx);

        // Decide once per bar, after every coin has closed it.
        if (c.OpenTime == _lastEval) return;
        foreach (var s in _p.Coins)
            if (!_open.TryGetValue(s, out var o) || o != c.OpenTime) return;
        _lastEval = c.OpenTime;

        var closeHour = (long)Math.Round((c.CloseTime.AddMilliseconds(1) - DateTime.UnixEpoch).TotalHours);
        if (closeHour % _p.RebalanceHours != 0) return;
        Rebalance(ctx);
    }

    /// <summary>Exit if this bar hit the stop set on an earlier bar; otherwise ratchet the stop up.</summary>
    private void TrailStop(Candle c, StrategyContext ctx)
    {
        if (_p.AtrStop <= 0 || ctx.Holding(c.Symbol) is not { } h) return;

        if (h.StopLoss is { } sl && c.Low <= sl)
        {
            if (ctx.SellAll(c.Symbol, Math.Min(c.Open, sl), $"Trailing stop hit ({sl:0.####})") is not null)
            {
                _heldSince.Remove(c.Symbol);
                _cooldown[c.Symbol] = ctx.Now.AddHours(Math.Max(24, _p.RebalanceHours));
                ctx.MarkStateChanged();
            }
            return;
        }

        var stop = c.Close - _p.AtrStop * _atr[c.Symbol].Value;
        if (h.StopLoss is null || stop > h.StopLoss) ctx.SetProtection(c.Symbol, stop, null);
    }

    private void Rebalance(StrategyContext ctx)
    {
        var btc = _closes[Btc];
        var need = (Math.Max(_p.CoinTrendDays, Math.Max(_p.VolatilityDays, _p.LookbackDays.Max())) + 1) * _barsPerDay + 1;
        if (btc.Count < Math.Max(need, _p.RegimeDays * _barsPerDay + 1)) return;

        // Scores are computed during warm-up too, so the Algorithms view has them right after a restart.
        _riskOn = btc[^1] >= Sma(btc, _p.RegimeDays * _barsPerDay);
        var vols = new Dictionary<string, decimal>();
        foreach (var s in _p.Coins)
        {
            var x = _closes[s];
            if (x.Count < need) { _scores.Remove(s); continue; }
            var vol = Volatility(x);
            vols[s] = vol;
            decimal score = 0;
            foreach (var d in _p.LookbackDays)
            {
                var n = d * _barsPerDay;
                score += (x[^1] / x[^(n + 1)] - 1) / (vol * (decimal)Math.Sqrt(n));
            }
            _scores[s] = score / _p.LookbackDays.Length;
        }
        if (ctx.IsWarmup) return;

        var held = _p.Coins.Where(ctx.HasPosition).ToList();
        if (!_riskOn)
        {
            foreach (var s in held) Exit(ctx, s, $"BTC below its {_p.RegimeDays}-day average → cash");
            return;
        }

        bool Eligible(string s, bool newEntry) =>
            _scores.TryGetValue(s, out var score) && score > 0
            && _closes[s][^1] >= Sma(_closes[s], _p.CoinTrendDays * _barsPerDay)
            && (!newEntry || !_cooldown.TryGetValue(s, out var until) || ctx.Now >= until);

        var ranked = _scores.Keys.OrderByDescending(s => _scores[s]).ToList();

        // Hysteresis: keep held coins that are still eligible and near the top (or still inside the minimum hold).
        var target = new List<string>();
        foreach (var s in held.OrderByDescending(s => _scores.GetValueOrDefault(s)))
        {
            if (!Eligible(s, false)) continue;
            var young = _heldSince.TryGetValue(s, out var since) && (ctx.Now - since).TotalHours < _p.MinHoldHours;
            if (ranked.IndexOf(s) + 1 <= _p.TopK + _p.RankBuffer || young) target.Add(s);
        }
        while (target.Count > _p.TopK) target.RemoveAt(target.Count - 1);
        foreach (var s in ranked)
        {
            if (target.Count >= _p.TopK) break;
            if (!target.Contains(s) && Eligible(s, true)) target.Add(s);
        }

        // Inverse-volatility weights; fewer eligible coins than TopK → the rest stays in cash.
        var weights = new Dictionary<string, decimal>();
        if (target.Count > 0)
        {
            var inv = target.ToDictionary(s => s, s => 1 / vols[s]);
            var sum = inv.Values.Sum();
            foreach (var s in target) weights[s] = inv[s] / sum * Math.Min(1m, (decimal)target.Count / _p.TopK);
        }

        var equity = ctx.Cash + _p.Coins.Sum(s => ctx.Holding(s) is { } h ? h.Quantity * _close[s] : 0);

        // Sells first (rotate out / trim), then buys.
        foreach (var s in held)
        {
            var h = ctx.Holding(s)!;
            var value = h.Quantity * _close[s];
            if (!weights.TryGetValue(s, out var w)) { Exit(ctx, s, $"Rotated out ({Describe(s)})"); continue; }
            var tgt = w * equity;
            if (value > tgt * (1 + _p.DriftBand) && value - tgt > 10)
                ctx.Sell(s, _close[s], (value - tgt) / _close[s], $"Trim to {w:P0} target weight");
        }
        foreach (var s in target.OrderByDescending(s => _scores[s]))
        {
            var value = ctx.Holding(s) is { } h ? h.Quantity * _close[s] : 0;
            var tgt = weights[s] * equity;
            if (value > 0 && value >= tgt * (1 - _p.DriftBand)) continue;
            var amount = Math.Min(ctx.Cash, tgt - value);
            if (amount < 10) continue;
            var isNew = value == 0;
            var reason = isNew ? $"Rotated in at {weights[s]:P0} weight ({Describe(s)})" : $"Top up to {weights[s]:P0} target weight";
            if (ctx.Buy(s, _close[s], amount, reason) is not null && isNew)
            {
                _heldSince[s] = ctx.Now;
                if (_p.AtrStop > 0) ctx.SetProtection(s, _close[s] - _p.AtrStop * _atr[s].Value, null);
                ctx.MarkStateChanged();
            }
        }
    }

    private void Exit(StrategyContext ctx, string s, string reason)
    {
        if (ctx.Holding(s) is not { } h) return;
        if (ctx.Sell(s, _close[s], h.Quantity, reason) is not null && !ctx.HasPosition(s))
        {
            _heldSince.Remove(s);
            ctx.MarkStateChanged();
        }
    }

    private string Describe(string s)
    {
        var rank = _scores.Keys.OrderByDescending(k => _scores[k]).ToList().IndexOf(s) + 1;
        return $"rank {rank}, score {_scores.GetValueOrDefault(s):0.00}";
    }

    private static decimal Sma(List<decimal> x, int n)
    {
        decimal sum = 0;
        for (var i = x.Count - n; i < x.Count; i++) sum += x[i];
        return sum / n;
    }

    /// <summary>Standard deviation of bar log returns over the volatility window.</summary>
    private decimal Volatility(List<decimal> x)
    {
        var n = _p.VolatilityDays * _barsPerDay;
        double sum = 0, sum2 = 0;
        for (var i = x.Count - n; i < x.Count; i++)
        {
            var r = Math.Log((double)(x[i] / x[i - 1]));
            sum += r;
            sum2 += r * r;
        }
        var mean = sum / n;
        return (decimal)Math.Sqrt(Math.Max(1e-12, sum2 / n - mean * mean));
    }

    public override IReadOnlyDictionary<string, decimal> Indicators()
    {
        var d = new Dictionary<string, decimal>();
        var btc = _closes[Btc];
        var n = _p.RegimeDays * _barsPerDay;
        if (btc.Count >= n)
        {
            var avg = Sma(btc, n);
            d[$"BTC vs {_p.RegimeDays}d avg %"] = Round((btc[^1] / avg - 1) * 100, 2);
        }
        d["RiskOn"] = _riskOn ? 1 : 0;
        foreach (var s in _p.Coins)
            if (_scores.TryGetValue(s, out var score)) d[$"{s[..^4]} score"] = Round(score, 3);
        return d;
    }

    public override string? SaveState() => JsonSerializer.Serialize(new State(_heldSince, _cooldown));

    public override void LoadState(string json)
    {
        var s = JsonSerializer.Deserialize<State>(json);
        _heldSince = s?.HeldSince ?? new();
        _cooldown = s?.Cooldown ?? new();
    }

    private sealed record State(Dictionary<string, DateTime> HeldSince, Dictionary<string, DateTime> Cooldown);
}
