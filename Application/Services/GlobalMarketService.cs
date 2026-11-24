using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Interface;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services;

/// <summary>An instrument shown in the dashboard's "World markets" panel.</summary>
public record GlobalInstrument(string Symbol, string Name, string Region, string Group);

/// <summary>
/// Process-wide holder of the latest global-markets snapshot. Singleton, so every request and the
/// <c>GlobalMarketsJob</c> share one cache and the provider is hit at most once per refresh.
/// </summary>
public class GlobalMarketStore
{
    private GlobalMarketOverviewDto? _snapshot;

    public GlobalMarketOverviewDto? Snapshot => Volatile.Read(ref _snapshot);

    public void Set(GlobalMarketOverviewDto snapshot) => Volatile.Write(ref _snapshot, snapshot);

    /// <summary>Serialises refreshes so concurrent requests don't stampede the provider.</summary>
    public SemaphoreSlim RefreshLock { get; } = new(1, 1);
}

/// <summary>
/// Builds the "World markets" overview: US close + futures, Asian indices, gold/silver/oil and
/// risk gauges, plus a one-line mood read for checking before the CSE opens.
/// </summary>
public class GlobalMarketService
{
    /// <summary>A request serves the cached snapshot if it's younger than this; otherwise it refreshes.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>Average move (in %) beyond which the mood is called risk-on / risk-off.</summary>
    private const decimal MoodThresholdPercent = 0.5m;

    private static readonly (string Key, string Title)[] Groups =
    {
        ("us", "United States"),
        ("asia", "Asia"),
        ("commodities", "Commodities"),
        ("risk", "Risk gauges"),
    };

    public static readonly IReadOnlyList<GlobalInstrument> Instruments = new[]
    {
        new GlobalInstrument("^GSPC", "S&P 500", "US", "us"),
        new GlobalInstrument("^IXIC", "Nasdaq", "US", "us"),
        new GlobalInstrument("^DJI", "Dow Jones", "US", "us"),
        new GlobalInstrument("ES=F", "S&P 500 futures", "US", "us"),
        new GlobalInstrument("NQ=F", "Nasdaq 100 futures", "US", "us"),

        new GlobalInstrument("^N225", "Nikkei 225", "Japan", "asia"),
        new GlobalInstrument("^HSI", "Hang Seng", "Hong Kong", "asia"),
        new GlobalInstrument("000001.SS", "Shanghai Composite", "China", "asia"),
        new GlobalInstrument("^KS11", "KOSPI", "South Korea", "asia"),
        new GlobalInstrument("^NSEI", "Nifty 50", "India", "asia"),
        new GlobalInstrument("^STI", "Straits Times", "Singapore", "asia"),

        new GlobalInstrument("GC=F", "Gold", "USD / oz", "commodities"),
        new GlobalInstrument("SI=F", "Silver", "USD / oz", "commodities"),
        new GlobalInstrument("BZ=F", "Brent crude", "USD / bbl", "commodities"),

        new GlobalInstrument("^VIX", "VIX (fear index)", "US", "risk"),
        new GlobalInstrument("DX-Y.NYB", "US Dollar index", "DXY", "risk"),
    };

    private const string UsFuturesSymbol = "ES=F";
    private const string GoldSymbol = "GC=F";

    private readonly IGlobalMarketProvider _provider;
    private readonly GlobalMarketStore _store;
    private readonly ILogger<GlobalMarketService> _logger;

    public GlobalMarketService(
        IGlobalMarketProvider provider,
        GlobalMarketStore store,
        ILogger<GlobalMarketService> logger)
    {
        _provider = provider;
        _store = store;
        _logger = logger;
    }

    /// <summary>Returns the cached overview, refreshing first if it's missing or stale.</summary>
    public async Task<GlobalMarketOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var snapshot = _store.Snapshot;
        if (IsFresh(snapshot)) return snapshot!;
        return await RefreshCoreAsync(onlyIfStale: true, ct);
    }

    /// <summary>Always fetches new quotes (used by the background job).</summary>
    public Task<GlobalMarketOverviewDto> RefreshAsync(CancellationToken ct = default) =>
        RefreshCoreAsync(onlyIfStale: false, ct);

    private static bool IsFresh(GlobalMarketOverviewDto? s) =>
        s?.UpdatedAt is { } at && DateTime.UtcNow - at < StaleAfter;

    private async Task<GlobalMarketOverviewDto> RefreshCoreAsync(bool onlyIfStale, CancellationToken ct)
    {
        await _store.RefreshLock.WaitAsync(ct);
        try
        {
            var previous = _store.Snapshot;
            // Another caller may have refreshed while we waited for the lock.
            if (onlyIfStale && IsFresh(previous)) return previous!;

            var fetched = await _provider.FetchQuotesAsync(Instruments.Select(i => i.Symbol), ct);
            if (fetched.Count == 0)
            {
                _logger.LogWarning("{Provider} returned no global market quotes; keeping the last snapshot",
                    _provider.ProviderName);
                return previous ?? new GlobalMarketOverviewDto();
            }

            if (fetched.Count < Instruments.Count)
            {
                _logger.LogInformation("{Provider} returned {Count}/{Total} global market quotes",
                    _provider.ProviderName, fetched.Count, Instruments.Count);
            }

            var overview = Build(fetched, previous);
            _store.Set(overview);
            return overview;
        }
        finally
        {
            _store.RefreshLock.Release();
        }
    }

    private static GlobalMarketOverviewDto Build(
        IReadOnlyList<ProviderMarketQuote> fetched,
        GlobalMarketOverviewDto? previous)
    {
        var bySymbol = fetched.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);

        // Symbols that failed this round fall back to their last known value rather than vanishing.
        var lastKnown = previous?.Groups
            .SelectMany(g => g.Quotes)
            .ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, GlobalMarketQuoteDto>(StringComparer.OrdinalIgnoreCase);

        var quotes = new List<(GlobalInstrument Instrument, GlobalMarketQuoteDto Quote)>();
        foreach (var inst in Instruments)
        {
            if (bySymbol.TryGetValue(inst.Symbol, out var raw))
                quotes.Add((inst, ToDto(inst, raw)));
            else if (lastKnown.TryGetValue(inst.Symbol, out var old))
                quotes.Add((inst, old));
        }

        var groups = Groups
            .Select(g => new GlobalMarketGroupDto
            {
                Key = g.Key,
                Title = g.Title,
                Quotes = quotes.Where(q => q.Instrument.Group == g.Key).Select(q => q.Quote).ToList(),
            })
            .Where(g => g.Quotes.Count > 0)
            .ToList();

        return new GlobalMarketOverviewDto
        {
            Groups = groups,
            Mood = BuildMood(quotes.Select(q => (q.Instrument, q.Quote)).ToList()),
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private static GlobalMarketQuoteDto ToDto(GlobalInstrument inst, ProviderMarketQuote raw)
    {
        var change = raw.Price - raw.PreviousClose;
        var pct = raw.ChangePercent ?? (raw.PreviousClose > 0 ? change / raw.PreviousClose * 100m : 0m);

        return new GlobalMarketQuoteDto
        {
            Symbol = inst.Symbol,
            Name = inst.Name,
            Region = inst.Region,
            Currency = raw.Currency,
            Price = Math.Round(raw.Price, 4),
            PreviousClose = Math.Round(raw.PreviousClose, 4),
            Change = Math.Round(change, 4),
            ChangePercent = Math.Round(pct, 2),
            IsOpen = raw.IsOpen,
            MarketTime = raw.MarketTime,
        };
    }

    /// <summary>
    /// Mood = 60% Asia's average move + 40% S&amp;P futures. Asia trades during Sri Lanka's morning and
    /// is the closest lead for the CSE; futures carry the US direction since its last close.
    /// </summary>
    private static GlobalMarketMoodDto BuildMood(List<(GlobalInstrument Instrument, GlobalMarketQuoteDto Quote)> quotes)
    {
        var asia = quotes.Where(q => q.Instrument.Group == "asia").Select(q => q.Quote.ChangePercent).ToList();
        decimal? asiaAvg = asia.Count > 0 ? Math.Round(asia.Average(), 2) : null;
        decimal? futures = quotes.FirstOrDefault(q => q.Instrument.Symbol == UsFuturesSymbol).Quote?.ChangePercent;
        decimal? gold = quotes.FirstOrDefault(q => q.Instrument.Symbol == GoldSymbol).Quote?.ChangePercent;

        decimal? score = (asiaAvg, futures) switch
        {
            ({ } a, { } f) => a * 0.6m + f * 0.4m,
            ({ } a, null) => a,
            (null, { } f) => f,
            _ => null,
        };

        var (label, tone) = score switch
        {
            null => ("Unknown", "neutral"),
            >= MoodThresholdPercent => ("Risk-on", "positive"),
            <= -MoodThresholdPercent => ("Risk-off", "negative"),
            _ => ("Mixed", "neutral"),
        };

        var parts = new List<string>();
        if (asiaAvg is { } aa) parts.Add($"Asia avg {Signed(aa)}");
        if (futures is { } ff) parts.Add($"US futures {Signed(ff)}");
        if (gold is { } gg) parts.Add($"Gold {Signed(gg)}");

        return new GlobalMarketMoodDto
        {
            Label = label,
            Tone = tone,
            Summary = string.Join(" · ", parts),
            AsiaAverageChangePercent = asiaAvg,
            UsFuturesChangePercent = futures,
        };
    }

    private static string Signed(decimal pct) => $"{(pct >= 0 ? "+" : "")}{pct:0.00}%";
}
