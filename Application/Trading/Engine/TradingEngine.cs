using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Engine;

/// <summary>
/// Runs every registered <see cref="ITradingStrategy"/> as an independent paper-trading bot.
///
/// Efficiency:
///   • one Binance WebSocket for all bots (streams are de-duplicated across strategies)
///   • events go through a single-reader channel, so strategy code runs on one thread with no contention
///   • each event is routed only to the bots subscribed to it (pre-built lookup tables)
///   • indicators are streaming/O(1); bots warm up from REST history at startup instead of waiting for live candles
///   • state is written to disk only when something changed (trade, signal, toggle)
/// </summary>
public sealed class TradingEngine : BackgroundService
{
    private static readonly TimeSpan FundingPollInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<TradingEngine> _logger;
    private readonly BinanceMarketClient _market;
    private readonly Channel<MarketEvent> _events =
        Channel.CreateUnbounded<MarketEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly Dictionary<CandleFeed, BotRunner[]> _candleRoutes = new();
    private readonly Dictionary<CandleFeed, int> _warmup = new();
    private readonly Dictionary<CandleFeed, DateTime> _lastOpenTime = new();
    private readonly Dictionary<string, BotRunner[]> _depthRoutes = new();
    private readonly Dictionary<string, BotRunner[]> _tradeRoutes = new();
    private readonly Dictionary<string, BotRunner[]> _fundingRoutes = new();
    private readonly ConcurrentDictionary<string, decimal> _lastPrice = new();

    public TradingEngine(IEnumerable<ITradingStrategy> strategies, IConfiguration config,
        IHttpClientFactory http, ILogger<TradingEngine> logger)
    {
        _logger = logger;
        _market = new BinanceMarketClient(http, logger);

        FeeRate = ReadDecimal(config, "PaperTrading:FeeRate", 0.001m, min: 0m, max: 0.01m);
        var initialBalance = ReadDecimal(config, "PaperTrading:InitialBalance", 1000m, min: 10m, max: 10_000_000m);
        var directory = config["PaperTrading:Directory"] ?? "PaperTrading";
        var legacyPath = config["PaperTrading:LegacyStateFile"] ?? "PaperTradingState.json";

        var bots = new List<BotRunner>();
        foreach (var strategy in strategies)
        {
            if (bots.Any(b => b.Strategy.Id == strategy.Id))
                throw new InvalidOperationException($"Duplicate strategy id '{strategy.Id}'.");

            var runner = new BotRunner(strategy, FeeRate, initialBalance, directory, logger);
            runner.Load(strategy.Id == Strategies.EmaRsiStrategy.StrategyId
                ? () => LegacyStateImporter.TryImport(legacyPath)
                : null);
            bots.Add(runner);
        }
        Bots = bots;

        BuildRoutes();
        _logger.LogInformation(
            "Trading engine: {Bots} bots, {Feeds} candle feeds, fee {Fee:P3}/side, initial balance {Balance}.",
            Bots.Count, _candleRoutes.Count, FeeRate, initialBalance);
    }

    public IReadOnlyList<BotRunner> Bots { get; }
    public decimal FeeRate { get; }

    public BotRunner? Find(string id) => Bots.FirstOrDefault(b => b.Strategy.Id == id);

    /// <summary>Latest price the engine has seen for a symbol (from candles / trades).</summary>
    public decimal? LastPrice(string symbol) => _lastPrice.TryGetValue(symbol, out var p) ? p : null;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield(); // don't block host startup

        // Start streaming first so nothing is missed while warming up; stale candles are de-duplicated.
        var streamTask = _market.RunStreamAsync(BuildStreamList(), _events.Writer, ct);
        var fundingTask = _fundingRoutes.Count > 0 ? PollFundingAsync(ct) : Task.CompletedTask;

        await WarmupAsync(ct);

        try
        {
            await foreach (var ev in _events.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await HandleAsync(ev, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Trading engine failed to handle {Event}.", ev.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }

        await Task.WhenAll(Swallow(streamTask), Swallow(fundingTask));
    }

    private async Task WarmupAsync(CancellationToken ct)
    {
        var fetches = _warmup.Select(async kv =>
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    return (Feed: kv.Key, Candles: await _market.GetClosedCandlesAsync(kv.Key, kv.Value + 1, null, null, ct));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Warm-up fetch failed for {Feed} (attempt {Attempt}).", kv.Key, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }
            return (Feed: kv.Key, Candles: new List<Candle>());
        });

        var results = await Task.WhenAll(fetches);

        // Replay all feeds interleaved in time order so multi-symbol strategies see a consistent timeline.
        var replay = results
            .SelectMany(r => r.Candles.Select(c => (r.Feed, Candle: c)))
            .OrderBy(x => x.Candle.CloseTime)
            .ThenBy(x => x.Feed.Symbol, StringComparer.Ordinal);

        var count = 0;
        foreach (var (feed, candle) in replay)
        {
            DispatchCandle(feed, candle, warmup: true);
            count++;
        }

        _logger.LogInformation("Trading engine warm-up complete: {Count} historical candles replayed.", count);
    }

    private async Task PollFundingAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(FundingPollInterval);
        do
        {
            foreach (var symbol in _fundingRoutes.Keys)
            {
                try
                {
                    if (await _market.GetFundingRateAsync(symbol, ct) is { } rate)
                        _events.Writer.TryWrite(new FundingEvent(rate));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Funding rate fetch failed for {Symbol}.", symbol);
                }
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    // ── Event handling (single reader thread) ────────────────────────────────

    private async Task HandleAsync(MarketEvent ev, CancellationToken ct)
    {
        switch (ev)
        {
            case CandleEvent ce:
                var feed = new CandleFeed(ce.Candle.Symbol, ce.Interval);
                if (!_candleRoutes.ContainsKey(feed)) return;

                // Reconnect gap: replay the missed candles as warm-up (no trades at stale prices).
                if (_lastOpenTime.TryGetValue(feed, out var last) && ce.Candle.OpenTime > last + feed.Duration)
                    await BackfillGapAsync(feed, last + feed.Duration, ce.Candle.OpenTime.AddMilliseconds(-1), ct);

                DispatchCandle(feed, ce.Candle, warmup: false);
                break;

            case DepthEvent de when _depthRoutes.TryGetValue(de.Depth.Symbol, out var depthBots):
                foreach (var bot in depthBots)
                    Run(bot, de.Depth.Time, warmup: false, (s, ctx) => s.OnDepth(de.Depth, ctx));
                break;

            case TradeEvent te when _tradeRoutes.TryGetValue(te.Trade.Symbol, out var tradeBots):
                _lastPrice[te.Trade.Symbol] = te.Trade.Price;
                var trade = te.Trade;
                foreach (var bot in tradeBots)
                    Run(bot, trade.Time, warmup: false, (s, ctx) => s.OnAggTrade(trade, ctx));
                break;

            case FundingEvent fe when _fundingRoutes.TryGetValue(fe.Funding.Symbol, out var fundingBots):
                foreach (var bot in fundingBots)
                    Run(bot, fe.Funding.Time, warmup: false, (s, ctx) => s.OnFunding(fe.Funding, ctx));
                break;
        }
    }

    private async Task BackfillGapAsync(CandleFeed feed, DateTime from, DateTime to, CancellationToken ct)
    {
        try
        {
            var missed = await _market.GetClosedCandlesAsync(feed, 1000, from, to, ct);
            foreach (var c in missed)
                DispatchCandle(feed, c, warmup: true);
            _logger.LogInformation("Backfilled {Count} missed {Feed} candles.", missed.Count, feed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gap backfill failed for {Feed}.", feed);
        }
    }

    private void DispatchCandle(CandleFeed feed, Candle candle, bool warmup)
    {
        if (_lastOpenTime.TryGetValue(feed, out var last) && candle.OpenTime <= last)
            return; // duplicate / already replayed

        _lastOpenTime[feed] = candle.OpenTime;
        _lastPrice[candle.Symbol] = candle.Close;

        foreach (var bot in _candleRoutes[feed])
            Run(bot, candle.CloseTime, warmup, (s, ctx) => s.OnCandle(candle, feed.Interval, ctx));
    }

    private void Run(BotRunner bot, DateTime time, bool warmup, Action<ITradingStrategy, StrategyContext> action)
    {
        lock (bot.Sync)
        {
            var ctx = bot.Context;
            ctx.IsWarmup = warmup;
            ctx.Now = time;
            ctx.Dirty = false;

            try
            {
                action(bot.Strategy, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Strategy {Bot} threw while handling market data.", bot.Strategy.Id);
            }

            if (!warmup) bot.Account.State.LastEventTime = time;
            if (ctx.Dirty) bot.Save();
        }
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private void BuildRoutes()
    {
        var candle = new Dictionary<CandleFeed, List<BotRunner>>();
        var depth = new Dictionary<string, List<BotRunner>>();
        var trades = new Dictionary<string, List<BotRunner>>();
        var funding = new Dictionary<string, List<BotRunner>>();

        foreach (var bot in Bots)
        {
            var feeds = bot.Strategy.Feeds;
            foreach (var f in feeds.Candles)
            {
                Add(candle, f, bot);
                _warmup[f] = Math.Min(1000, Math.Max(_warmup.GetValueOrDefault(f), feeds.WarmupCandles));
            }
            foreach (var s in feeds.Depth) Add(depth, s, bot);
            foreach (var s in feeds.AggTrades) Add(trades, s, bot);
            foreach (var s in feeds.Funding) Add(funding, s, bot);
        }

        foreach (var kv in candle) _candleRoutes[kv.Key] = kv.Value.ToArray();
        foreach (var kv in depth) _depthRoutes[kv.Key] = kv.Value.ToArray();
        foreach (var kv in trades) _tradeRoutes[kv.Key] = kv.Value.ToArray();
        foreach (var kv in funding) _fundingRoutes[kv.Key] = kv.Value.ToArray();

        static void Add<TKey>(Dictionary<TKey, List<BotRunner>> map, TKey key, BotRunner bot) where TKey : notnull
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<BotRunner>();
            if (!list.Contains(bot)) list.Add(bot);
        }
    }

    private List<string> BuildStreamList()
    {
        var streams = _candleRoutes.Keys.Select(f => f.StreamName).ToList();
        streams.AddRange(_depthRoutes.Keys.Select(s => $"{s.ToLowerInvariant()}@depth20"));
        streams.AddRange(_tradeRoutes.Keys.Select(s => $"{s.ToLowerInvariant()}@aggTrade"));
        return streams;
    }

    private decimal ReadDecimal(IConfiguration config, string key, decimal fallback, decimal min, decimal max)
    {
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max)
            return v;
        _logger.LogWarning("Invalid {Key} '{Raw}', using {Fallback}.", key, raw, fallback);
        return fallback;
    }

    private static async Task Swallow(Task task)
    {
        try { await task; } catch { /* shutting down */ }
    }
}
