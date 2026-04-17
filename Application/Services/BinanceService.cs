using Microsoft.Extensions.Hosting;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks;


public class BinanceService : BackgroundService
{
    private readonly ILogger<BinanceService> _logger;
    private readonly ConcurrentDictionary<string, decimal> _prices = new();
    public IReadOnlyDictionary<string, decimal> Prices => _prices;
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdated = new();
    private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(1);

    public event Func<Candle, Task>? CandleClosed;

    private readonly ConcurrentDictionary<string, DateTime> _candleCache = new();
    private readonly TimeSpan _cacheRetention = TimeSpan.FromHours(2);

    // private readonly string _url =
    //     "wss://stream.binance.com:9443/stream?streams=" +
    //     "btcusdt@ticker/ethusdt@ticker/solusdt@ticker";

    private readonly string _url =
        "wss://stream.binance.com:9443/ws/btcusdt@kline_1m";

    public BinanceService(ILogger<BinanceService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => CleanupCacheLoop(stoppingToken));
        while (!stoppingToken.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(_url), stoppingToken);
                var buffer = new byte[8192];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await ws.ReceiveAsync(buffer, stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogWarning("WebSocket closed by server. Reconnecting...");
                            break;
                        }
                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessCryptoCandles(msg);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in Binance WebSocket receive loop");
                        break; // Exit inner loop to reconnect
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to Binance WebSocket");
            }
            _logger.LogInformation("Reconnecting to Binance WebSocket in 5 seconds...");
            await Task.Delay(5000, stoppingToken);
        }
    }

    private void ProcessCryptoData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            var data = doc.RootElement.GetProperty("data");
            var symbol = data.GetProperty("s").GetString();

            if (string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogWarning("Received ticker update with missing symbol: {Json}", json);
                return;
            }

            var lastPriceString = data.GetProperty("c").GetString();
            var now = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(symbol))
            {
                return; // or continue
            }
            // Check throttling
            if (_lastUpdated.TryGetValue(symbol, out var lastTime))
            {
                if (now - lastTime < _updateInterval)
                    return; // skip update
            }

            if (decimal.TryParse(lastPriceString, out var lastPrice))
            {
                _prices[symbol] = lastPrice;
                _lastUpdated[symbol] = now;
                Console.WriteLine($"Updated {symbol} price to {lastPrice}");
            }
            else
            {
                _logger.LogWarning($"Could not parse lastPrice for {symbol}: {lastPriceString}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Binance WebSocket message");
        }
    }

    private async Task ProcessCryptoCandles(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var k = root.GetProperty("k");

            if (!k.GetProperty("x").GetBoolean())
                return;

            var symbol = k.GetProperty("s").GetString();
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("t").GetInt64()).UtcDateTime;

            if (string.IsNullOrEmpty(symbol))
                return;

            // // ✅ dedupe key BEFORE building object
            var key = $"{symbol}:{openTime:O}";

            if (!_candleCache.TryAdd(key, DateTime.UtcNow))
                return;

            var candle = new Candle
            {
                Symbol = k.GetProperty("s").GetString()!,
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("t").GetInt64()).UtcDateTime,
                CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("T").GetInt64()).UtcDateTime,

                Open = decimal.Parse(k.GetProperty("o").GetString()!),
                High = decimal.Parse(k.GetProperty("h").GetString()!),
                Low = decimal.Parse(k.GetProperty("l").GetString()!),
                Close = decimal.Parse(k.GetProperty("c").GetString()!),
                Volume = decimal.Parse(k.GetProperty("v").GetString()!)
            };

            if (CandleClosed != null)
                await CandleClosed.Invoke(candle);

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process candle");
        }
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
