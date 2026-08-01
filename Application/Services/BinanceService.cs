using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

/// <summary>
/// BackgroundService that maintains two Binance WebSocket connections:
///   1. kline_5m (BTCUSDT) — fires CandleClosed event consumed by TradingBotService (unchanged behaviour)
///   2. !miniTicker@arr — streams 24h stats for all symbols, filtered to tracked pairs
///
/// Real-time data is pushed to connected browser clients via SignalR (CryptoHub).
/// </summary>
public class BinanceService : BackgroundService
{
    private readonly ILogger<BinanceService> _logger;
    private readonly IHubContext<CryptoHub>  _hub;

    // ── Legacy price store (keep for backward-compat with existing code) ──────
    private readonly ConcurrentDictionary<string, decimal> _prices = new();
    public IReadOnlyDictionary<string, decimal> Prices => _prices;

    // ── 24h Ticker store — populated by miniTicker stream ────────────────────
    private readonly ConcurrentDictionary<string, TickerData> _tickers = new();
    public IReadOnlyDictionary<string, TickerData> Tickers => _tickers;

    // ── Symbols tracked for the dashboard ────────────────────────────────────
    public static readonly string[] TrackedSymbols =
        ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT", "DOGEUSDT"];

    // ── CandleClosed event — consumed by TradingBotService (DO NOT REMOVE) ───
    public event Func<Candle, Task>? CandleClosed;

    // ── Internal dedup cache for candles ─────────────────────────────────────
    private readonly ConcurrentDictionary<string, DateTime> _candleCache     = new();
    private readonly TimeSpan                               _cacheRetention  = TimeSpan.FromHours(2);

    // ── Stream URLs ───────────────────────────────────────────────────────────
    private const string KlineUrl  = "wss://stream.binance.com:9443/ws/btcusdt@kline_5m";
    private const string TickerUrl = "wss://stream.binance.com:9443/ws/!miniTicker@arr";

    public BinanceService(ILogger<BinanceService> logger, IHubContext<CryptoHub> hub)
    {
        _logger = logger;
        _hub    = hub;
    }

    // ── Public accessors ──────────────────────────────────────────────────────

    public TickerData? GetTicker(string symbol) =>
        _tickers.TryGetValue(symbol.ToUpperInvariant(), out var t) ? t : null;

    public IEnumerable<TickerData> GetAllTickers() =>
        TrackedSymbols
            .Select(s => _tickers.TryGetValue(s, out var t) ? t : null)
            .Where(t => t != null)
            .Select(t => t!);

    // ── Background entry point ────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cache cleanup runs in background
        _ = Task.Run(() => CleanupCacheLoop(stoppingToken), stoppingToken);

        // Run kline and ticker streams concurrently
        await Task.WhenAll(
            RunKlineStreamAsync(stoppingToken),
            RunTickerStreamAsync(stoppingToken)
        );
    }

    // ── Kline stream (BTCUSDT 5m) ─────────────────────────────────────────────

    private async Task RunKlineStreamAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(KlineUrl), stoppingToken);
                _logger.LogInformation("Binance kline WebSocket connected.");

                var buffer = new byte[8192];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await ws.ReceiveAsync(buffer, stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogWarning("Kline WebSocket closed by server. Reconnecting…");
                            break;
                        }
                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessCryptoCandles(msg);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in kline receive loop");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kline WebSocket connection error");
            }

            _logger.LogInformation("Kline stream reconnecting in 5s…");
            await Task.Delay(5_000, stoppingToken);
        }
    }

    // ── Ticker stream (all symbols, 24h stats) ────────────────────────────────

    private async Task RunTickerStreamAsync(CancellationToken stoppingToken)
    {
        DateTime lastHubPush = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(TickerUrl), stoppingToken);
                _logger.LogInformation("Binance miniTicker WebSocket connected.");

                // miniTicker@arr sends large payloads — accumulate chunks
                var ms     = new System.IO.MemoryStream();
                var buffer = new byte[32_768];

                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        ms.SetLength(0);
                        WebSocketReceiveResult result;

                        do
                        {
                            result = await ws.ReceiveAsync(buffer, stoppingToken);
                            if (result.MessageType == WebSocketMessageType.Close) break;
                            ms.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close) break;

                        var msg = Encoding.UTF8.GetString(ms.ToArray());
                        ProcessTickerArray(msg);

                        // Push throttled ticker update to SignalR clients (≤ every 2 seconds)
                        if ((DateTime.UtcNow - lastHubPush).TotalSeconds >= 2)
                        {
                            lastHubPush = DateTime.UtcNow;
                            var payload = GetAllTickers().Select(t => new
                            {
                                symbol             = t.Symbol,
                                lastPrice          = t.LastPrice,
                                priceChangePercent = Math.Round(t.PriceChangePercent, 2),
                                high24h            = t.High24h,
                                low24h             = t.Low24h,
                                quoteVolume        = t.QuoteVolume24h
                            });
                            await _hub.Clients.All.SendAsync("TickerUpdate", payload, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in ticker receive loop");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ticker WebSocket connection error");
            }

            _logger.LogInformation("Ticker stream reconnecting in 5s…");
            await Task.Delay(5_000, stoppingToken);
        }
    }

    // ── Process 24h mini-ticker array ────────────────────────────────────────

    private void ProcessTickerArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("s", out var sProp)) continue;
                var symbol = sProp.GetString()!;

                if (!TrackedSymbols.Contains(symbol)) continue;

                var close = ParseDecimal(item, "c");
                var open  = ParseDecimal(item, "o");
                var pct   = open > 0 ? (close - open) / open * 100 : 0m;

                var ticker = new TickerData
                {
                    Symbol             = symbol,
                    LastPrice          = close,
                    OpenPrice          = open,
                    PriceChange        = close - open,
                    PriceChangePercent = Math.Round(pct, 2),
                    High24h            = ParseDecimal(item, "h"),
                    Low24h             = ParseDecimal(item, "l"),
                    Volume24h          = ParseDecimal(item, "v"),
                    QuoteVolume24h     = ParseDecimal(item, "q"),
                    UpdatedAt          = DateTime.UtcNow
                };

                _tickers[symbol] = ticker;
                _prices[symbol]  = ticker.LastPrice;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process ticker array");
        }
    }

    // ── Process BTCUSDT kline — fires CandleClosed + SignalR push ─────────────

    private async Task ProcessCryptoCandles(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var k    = root.GetProperty("k");

            // Only process fully-closed candles
            if (!k.GetProperty("x").GetBoolean())
                return;

            var symbol   = k.GetProperty("s").GetString();
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("t").GetInt64()).UtcDateTime;

            if (string.IsNullOrEmpty(symbol))
                return;

            // Deduplicate
            var key = $"{symbol}:{openTime:O}";
            if (!_candleCache.TryAdd(key, DateTime.UtcNow))
                return;

            var candle = new Candle
            {
                Symbol    = k.GetProperty("s").GetString()!,
                OpenTime  = openTime,
                CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("T").GetInt64()).UtcDateTime,
                Open      = decimal.Parse(k.GetProperty("o").GetString()!),
                High      = decimal.Parse(k.GetProperty("h").GetString()!),
                Low       = decimal.Parse(k.GetProperty("l").GetString()!),
                Close     = decimal.Parse(k.GetProperty("c").GetString()!),
                Volume    = decimal.Parse(k.GetProperty("v").GetString()!)
            };

            // ① Fire existing event — TradingBotService subscribes here (DO NOT REMOVE)
            if (CandleClosed != null)
                await CandleClosed.Invoke(candle);

            // ② Push live candle update to dashboard via SignalR
            await _hub.Clients.All.SendAsync("CandleUpdate", new
            {
                symbol   = candle.Symbol,
                time     = new DateTimeOffset(candle.OpenTime).ToUnixTimeSeconds(),
                open     = candle.Open,
                high     = candle.High,
                low      = candle.Low,
                close    = candle.Close,
                volume   = candle.Volume
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process candle");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static decimal ParseDecimal(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0m;
        var str = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        return decimal.TryParse(str, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private async Task CleanupCacheLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var threshold = DateTime.UtcNow - _cacheRetention;
                foreach (var item in _candleCache)
                {
                    if (item.Value < threshold)
                        _candleCache.TryRemove(item.Key, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache cleanup failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), token);
        }
    }
}
