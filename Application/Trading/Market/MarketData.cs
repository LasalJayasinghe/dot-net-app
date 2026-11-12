namespace dotnetApp.Application.Trading.Market;

/// <summary>A Binance kline subscription, e.g. BTCUSDT @ 1h.</summary>
public readonly record struct CandleFeed(string Symbol, string Interval)
{
    public string StreamName => $"{Symbol.ToLowerInvariant()}@kline_{Interval}";

    public TimeSpan Duration => Intervals.ToTimeSpan(Interval);

    public override string ToString() => $"{Symbol} {Interval}";
}

/// <summary>Top-20 order book summary (Binance partial depth stream).</summary>
public sealed record DepthSnapshot(string Symbol, decimal BidQty, decimal AskQty, decimal BestBid, decimal BestAsk, DateTime Time)
{
    /// <summary>-1 (all asks) … +1 (all bids).</summary>
    public decimal Imbalance => BidQty + AskQty == 0 ? 0 : (BidQty - AskQty) / (BidQty + AskQty);
}

/// <summary>Aggregated taker trade (Binance aggTrade stream).</summary>
public readonly record struct AggTrade(string Symbol, decimal Price, decimal Quantity, bool IsTakerBuy, DateTime Time)
{
    public decimal QuoteValue => Price * Quantity;
}

/// <summary>USDⓈ-M perpetual funding rate (Binance futures premiumIndex).</summary>
public sealed record FundingRate(string Symbol, decimal Rate, decimal MarkPrice, DateTime NextFundingTime, DateTime Time);

/// <summary>Market data a strategy subscribes to.</summary>
public sealed class StrategyFeeds
{
    public IReadOnlyList<CandleFeed> Candles { get; init; } = Array.Empty<CandleFeed>();

    /// <summary>How many historical candles per feed to replay at startup before trading live.</summary>
    public int WarmupCandles { get; init; } = 100;

    public IReadOnlyList<string> Depth { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AggTrades { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Funding { get; init; } = Array.Empty<string>();
}

public static class Intervals
{
    public static TimeSpan ToTimeSpan(string interval) => interval switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "3m" => TimeSpan.FromMinutes(3),
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "2h" => TimeSpan.FromHours(2),
        "4h" => TimeSpan.FromHours(4),
        "1d" => TimeSpan.FromDays(1),
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported Binance interval"),
    };
}

public static class Symbols
{
    /// <summary>"BTCUSDT" → "BTC/USDT".</summary>
    public static string ToPair(string symbol) =>
        symbol.EndsWith("USDT", StringComparison.Ordinal) ? $"{symbol[..^4]}/USDT" : symbol;
}
