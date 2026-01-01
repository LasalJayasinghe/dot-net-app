using Microsoft.Extensions.Hosting;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;


public class BinanceService : BackgroundService
{
    private readonly ILogger<BinanceService> _logger;
    private readonly ConcurrentDictionary<string, decimal> _prices = new();
    public IReadOnlyDictionary<string, decimal> Prices => _prices;
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdated = new();
    private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(1);

    private readonly string _url =
        "wss://stream.binance.com:9443/stream?streams=" +
        "btcusdt@ticker/ethusdt@ticker/solusdt@ticker";

    public BinanceService(ILogger<BinanceService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_url), stoppingToken);

        var buffer = new byte[8192];

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, stoppingToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.ConnectAsync(new Uri(_url), stoppingToken);
                continue;
            }

            var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
            ProcessMessage(msg);
        }
    }

    private void ProcessMessage(string json)
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

                _logger.LogInformation($"{symbol} → {lastPrice}");
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

}
