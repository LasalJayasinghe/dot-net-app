using System.Buffers;
using System.Buffers.Text;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using dotnetApp.Application.Trading.Market;

namespace dotnetApp.Application.Trading.Engine;

public abstract record MarketEvent;
public sealed record CandleEvent(Candle Candle, string Interval) : MarketEvent;
public sealed record DepthEvent(DepthSnapshot Depth) : MarketEvent;
public sealed record TradeEvent(AggTrade Trade) : MarketEvent;
public sealed record FundingEvent(FundingRate Funding) : MarketEvent;

/// <summary>
/// Public (key-less) Binance market data for the trading engine:
///   • ONE combined WebSocket carrying every kline / depth / aggTrade stream the bots need
///   • REST klines for warm-up and gap backfill
///   • futures premiumIndex polling for funding rates
/// JSON is parsed straight from the receive buffer (no string copies; decimals parsed from UTF-8).
/// </summary>
public sealed class BinanceMarketClient
{
    private const string StreamBase = "wss://stream.binance.com:9443/stream?streams=";
    private const string KlinesUrl = "https://api.binance.com/api/v3/klines";
    private const string PremiumIndexUrl = "https://fapi.binance.com/fapi/v1/premiumIndex";

    private readonly IHttpClientFactory _http;
    private readonly ILogger _logger;

    public BinanceMarketClient(IHttpClientFactory http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    // ── WebSocket ────────────────────────────────────────────────────────────

    public async Task RunStreamAsync(IReadOnlyCollection<string> streams, ChannelWriter<MarketEvent> writer, CancellationToken ct)
    {
        if (streams.Count == 0) return;

        var url = new Uri(StreamBase + string.Join('/', streams));
        var backoff = TimeSpan.FromSeconds(2);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                try
                {
                    await ws.ConnectAsync(url, ct);
                    _logger.LogInformation("Trading engine stream connected ({Count} streams).", streams.Count);
                    backoff = TimeSpan.FromSeconds(2);

                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var length = 0;
                        ValueWebSocketReceiveResult result;
                        do
                        {
                            if (length == buffer.Length)
                            {
                                var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                                Buffer.BlockCopy(buffer, 0, bigger, 0, length);
                                ArrayPool<byte>.Shared.Return(buffer);
                                buffer = bigger;
                            }
                            result = await ws.ReceiveAsync(buffer.AsMemory(length), ct);
                            length += result.Count;
                        } while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

                        if (result.MessageType == WebSocketMessageType.Close) break;

                        var ev = Parse(buffer.AsMemory(0, length));
                        if (ev is not null) writer.TryWrite(ev);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Trading engine stream error.");
                }

                _logger.LogInformation("Trading engine stream reconnecting in {Delay}s…", backoff.TotalSeconds);
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private MarketEvent? Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("stream", out var streamProp) || !root.TryGetProperty("data", out var data))
                return null;

            var stream = streamProp.GetString()!;
            var at = stream.IndexOf('@');
            var symbol = stream[..at].ToUpperInvariant();
            var kind = stream.AsSpan(at + 1);

            if (kind.StartsWith("kline_"))
            {
                var k = data.GetProperty("k");
                if (!k.GetProperty("x").GetBoolean()) return null; // only closed candles
                return new CandleEvent(ParseKline(symbol, k), k.GetProperty("i").GetString()!);
            }

            if (kind.StartsWith("depth"))
                return new DepthEvent(ParseDepth(symbol, data));

            if (kind.SequenceEqual("aggTrade"))
            {
                return new TradeEvent(new AggTrade(
                    symbol,
                    Dec(data.GetProperty("p")),
                    Dec(data.GetProperty("q")),
                    IsTakerBuy: !data.GetProperty("m").GetBoolean(), // m = buyer is maker → taker sold
                    FromMs(data.GetProperty("T").GetInt64())));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse market message.");
        }
        return null;
    }

    private static Candle ParseKline(string symbol, JsonElement k) => new()
    {
        Symbol = symbol,
        OpenTime = FromMs(k.GetProperty("t").GetInt64()),
        CloseTime = FromMs(k.GetProperty("T").GetInt64()),
        Open = Dec(k.GetProperty("o")),
        High = Dec(k.GetProperty("h")),
        Low = Dec(k.GetProperty("l")),
        Close = Dec(k.GetProperty("c")),
        Volume = Dec(k.GetProperty("v")),
    };

    private static DepthSnapshot ParseDepth(string symbol, JsonElement data)
    {
        decimal bidQty = 0, askQty = 0, bestBid = 0, bestAsk = 0;
        var first = true;
        foreach (var level in data.GetProperty("bids").EnumerateArray())
        {
            if (first) { bestBid = Dec(level[0]); first = false; }
            bidQty += Dec(level[1]);
        }
        first = true;
        foreach (var level in data.GetProperty("asks").EnumerateArray())
        {
            if (first) { bestAsk = Dec(level[0]); first = false; }
            askQty += Dec(level[1]);
        }
        return new DepthSnapshot(symbol, bidQty, askQty, bestBid, bestAsk, DateTime.UtcNow);
    }

    // ── REST ─────────────────────────────────────────────────────────────────

    /// <summary>Closed klines only (the still-forming last candle is dropped).</summary>
    public async Task<List<Candle>> GetClosedCandlesAsync(CandleFeed feed, int limit, DateTime? startTime,
        DateTime? endTime, CancellationToken ct)
    {
        var url = $"{KlinesUrl}?symbol={feed.Symbol}&interval={feed.Interval}&limit={Math.Clamp(limit, 1, 1000)}";
        if (startTime is { } s) url += $"&startTime={ToMs(s)}";
        if (endTime is { } e) url += $"&endTime={ToMs(e)}";

        var client = _http.CreateClient("binance");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        var now = DateTime.UtcNow;
        var candles = new List<Candle>(doc.RootElement.GetArrayLength());
        foreach (var k in doc.RootElement.EnumerateArray())
        {
            var closeTime = FromMs(k[6].GetInt64());
            if (closeTime > now) continue; // still forming
            candles.Add(new Candle
            {
                Symbol = feed.Symbol,
                OpenTime = FromMs(k[0].GetInt64()),
                CloseTime = closeTime,
                Open = Dec(k[1]),
                High = Dec(k[2]),
                Low = Dec(k[3]),
                Close = Dec(k[4]),
                Volume = Dec(k[5]),
            });
        }
        return candles;
    }

    public async Task<FundingRate?> GetFundingRateAsync(string symbol, CancellationToken ct)
    {
        var client = _http.CreateClient("binance");
        using var response = await client.GetAsync($"{PremiumIndexUrl}?symbol={symbol}", ct);
        if (!response.IsSuccessStatusCode) return null;
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        var r = doc.RootElement;
        return new FundingRate(
            symbol,
            Dec(r.GetProperty("lastFundingRate")),
            Dec(r.GetProperty("markPrice")),
            FromMs(r.GetProperty("nextFundingTime").GetInt64()),
            FromMs(r.GetProperty("time").GetInt64()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Parses a Binance numeric (usually a quoted string) without allocating.</summary>
    private static decimal Dec(JsonElement e)
    {
        var raw = JsonMarshal.GetRawUtf8Value(e);
        if (raw.Length >= 2 && raw[0] == (byte)'"') raw = raw[1..^1];
        return Utf8Parser.TryParse(raw, out decimal value, out _) ? value : 0m;
    }

    private static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    private static long ToMs(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
