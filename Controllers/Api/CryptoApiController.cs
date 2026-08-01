using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace dotnetApp.Controllers.Api;

/// <summary>
/// REST API consumed exclusively by the Crypto Trading Dashboard frontend.
///
/// Endpoints:
///   GET /api/crypto/tickers          — 24h market cards data
///   GET /api/crypto/candles          — OHLCV history for charting (Binance REST backfill)
///   GET /api/crypto/strategy         — EMA/RSI strategy snapshot
///   GET /api/crypto/scanner          — Multi-symbol opportunity scan
///   GET /api/crypto/whales           — Large trade detection
///   GET /api/crypto/ai-summary       — Rule-based market analysis text
/// </summary>
[ApiController]
[Route("api/crypto")]
[Authorize]
public class CryptoApiController : ControllerBase
{
    private readonly BinanceService       _binance;
    private readonly ICryptoMarketService _cryptoMarket;
    private readonly IAiMarketSummaryService _aiSummary;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly IMemoryCache         _cache;
    private readonly ILogger<CryptoApiController> _logger;

    private static readonly string[] ScannerSymbols =
        ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT", "DOGEUSDT"];

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CryptoApiController(
        BinanceService          binance,
        ICryptoMarketService    cryptoMarket,
        IAiMarketSummaryService aiSummary,
        IHttpClientFactory      httpFactory,
        IMemoryCache            cache,
        ILogger<CryptoApiController> logger)
    {
        _binance      = binance;
        _cryptoMarket = cryptoMarket;
        _aiSummary    = aiSummary;
        _httpFactory  = httpFactory;
        _cache        = cache;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/crypto/tickers
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("tickers")]
    public IActionResult GetTickers()
    {
        var tickers = _binance.GetAllTickers().Select(t => new
        {
            symbol             = t.Symbol,
            lastPrice          = t.LastPrice,
            priceChangePercent = t.PriceChangePercent,
            high24h            = t.High24h,
            low24h             = t.Low24h,
            quoteVolume        = t.QuoteVolume24h,
            updatedAt          = t.UpdatedAt
        });

        return Ok(tickers);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/crypto/candles?symbol=BTCUSDT&interval=5m&limit=200
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("candles")]
    public async Task<IActionResult> GetCandles(
        [FromQuery] string symbol   = "BTCUSDT",
        [FromQuery] string interval = "5m",
        [FromQuery] int    limit    = 200)
    {
        symbol   = symbol.ToUpperInvariant();
        interval = interval.ToLowerInvariant();
        limit    = Math.Clamp(limit, 1, 1000);

        var cacheKey = $"candles:{symbol}:{interval}:{limit}";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Ok(cached);

        try
        {
            var client = _httpFactory.CreateClient("binance");
            var url    = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
            var json   = await client.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var candles   = doc.RootElement.EnumerateArray().Select(k =>
            {
                var arr = k.EnumerateArray().ToArray();
                return new
                {
                    time   = arr[0].GetInt64() / 1000,          // seconds for Lightweight Charts
                    open   = decimal.Parse(arr[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    high   = decimal.Parse(arr[2].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    low    = decimal.Parse(arr[3].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    close  = decimal.Parse(arr[4].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    volume = decimal.Parse(arr[5].GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                };
            }).ToList();

            _cache.Set(cacheKey, candles, TimeSpan.FromSeconds(15));
            return Ok(candles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch candles for {Symbol} {Interval}", symbol, interval);
            return StatusCode(503, new { message = "Unable to fetch candle data from Binance." });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/crypto/strategy?symbol=BTCUSDT
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("strategy")]
    public async Task<IActionResult> GetStrategy([FromQuery] string symbol = "BTCUSDT")
    {
        symbol = symbol.ToUpperInvariant();

        try
        {
            var candles = await FetchCandlesAsync(symbol, "5m", 100);
            var snapshot = _cryptoMarket.GetStrategySnapshot(symbol, candles);

            return Ok(new
            {
                symbol          = snapshot.Symbol,
                ema9            = snapshot.Ema9,
                ema21           = snapshot.Ema21,
                rsi             = snapshot.Rsi,
                currentPrice    = snapshot.CurrentPrice,
                signal          = snapshot.Signal,
                reason          = snapshot.Reason,
                marketCondition = snapshot.MarketCondition,
                confidence      = snapshot.Confidence,
                evaluatedAt     = snapshot.EvaluatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Strategy evaluation failed for {Symbol}", symbol);
            return StatusCode(503, new { message = "Strategy evaluation failed." });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/crypto/scanner
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("scanner")]
    public async Task<IActionResult> GetScanner()
    {
        const string cacheKey = "scanner:all";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Ok(cached);

        try
        {
            // Fetch candles for all tracked symbols in parallel
            var tasks = ScannerSymbols.Select(s => FetchCandlesAsync(s, "5m", 100)
                .ContinueWith(t => (symbol: s, candles: t.Result)));

            var results  = await Task.WhenAll(tasks);
            var candleMap = results.ToDictionary(r => r.symbol, r => r.candles);

            var scanResults = _cryptoMarket.GetScannerResults(candleMap).Select(r => new
            {
                symbol          = r.Symbol,
                signal          = r.Signal,
                confidence      = r.Confidence,
                price           = r.Price,
                marketCondition = r.MarketCondition,
                rsi             = r.Rsi,
                ema9            = r.Ema9,
                ema21           = r.Ema21
            }).ToList();

            _cache.Set(cacheKey, scanResults, TimeSpan.FromSeconds(30));
            return Ok(scanResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Market scanner failed");
            return StatusCode(503, new { message = "Market scanner temporarily unavailable." });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/crypto/whales?symbol=BTCUSDT&minUsd=100000
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("whales")]
    public async Task<IActionResult> GetWhales(
        [FromQuery] string symbol = "BTCUSDT",
        [FromQuery] double minUsd = 100_000)
    {
        symbol = symbol.ToUpperInvariant();
        var cacheKey = $"whales:{symbol}:{minUsd}";

        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Ok(cached);

        try
        {
            var client = _httpFactory.CreateClient("binance");
            var url    = $"https://api.binance.com/api/v3/trades?symbol={symbol}&limit=1000";
            var json   = await client.GetStringAsync(url);

            using var doc  = JsonDocument.Parse(json);
            var whales = doc.RootElement.EnumerateArray()
                .Select(t => new
                {
                    id           = t.GetProperty("id").GetInt64(),
                    price        = decimal.Parse(t.GetProperty("price").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    qty          = decimal.Parse(t.GetProperty("qty").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    quoteQty     = decimal.Parse(t.GetProperty("quoteQty").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    time         = DateTimeOffset.FromUnixTimeMilliseconds(t.GetProperty("time").GetInt64()).UtcDateTime,
                    // isBuyerMaker=false → aggressive BUY; true → aggressive SELL
                    side         = t.GetProperty("isBuyerMaker").GetBoolean() ? "SELL" : "BUY"
                })
                .Where(t => (double)t.quoteQty >= minUsd)
                .OrderByDescending(t => t.time)
                .Take(30)
                .Select(t => new
                {
                    symbol   = symbol,
                    side     = t.side,
                    amount   = t.quoteQty,
                    price    = t.price,
                    qty      = t.qty,
                    time     = t.time
                })
                .ToList();

            _cache.Set(cacheKey, whales, TimeSpan.FromSeconds(10));
            return Ok(whales);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whale fetch failed for {Symbol}", symbol);
            return StatusCode(503, new { message = "Whale activity data unavailable." });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/crypto/ai-summary?symbol=BTCUSDT
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("ai-summary")]
    public async Task<IActionResult> GetAiSummary([FromQuery] string symbol = "BTCUSDT")
    {
        symbol = symbol.ToUpperInvariant();

        try
        {
            var candles  = await FetchCandlesAsync(symbol, "5m", 100);
            var snapshot = _cryptoMarket.GetStrategySnapshot(symbol, candles);

            var summary = _aiSummary.GenerateSummary(
                symbol    : snapshot.Symbol,
                price     : snapshot.CurrentPrice,
                ema9      : snapshot.Ema9,
                ema21     : snapshot.Ema21,
                rsi       : snapshot.Rsi,
                condition : snapshot.MarketCondition
            );

            return Ok(new
            {
                symbol      = snapshot.Symbol,
                summary     = summary,
                generatedAt = DateTime.UtcNow,
                indicators  = new { ema9 = snapshot.Ema9, ema21 = snapshot.Ema21, rsi = snapshot.Rsi }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI summary failed for {Symbol}", symbol);
            return StatusCode(503, new { message = "AI summary temporarily unavailable." });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<Candle>> FetchCandlesAsync(string symbol, string interval, int limit)
    {
        var cacheKey = $"candles_raw:{symbol}:{interval}:{limit}";
        if (_cache.TryGetValue(cacheKey, out List<Candle>? cached) && cached != null)
            return cached;

        var client = _httpFactory.CreateClient("binance");
        var url    = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var json   = await client.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var candles   = doc.RootElement.EnumerateArray().Select(k =>
        {
            var arr = k.EnumerateArray().ToArray();
            return new Candle
            {
                Symbol    = symbol,
                OpenTime  = DateTimeOffset.FromUnixTimeMilliseconds(arr[0].GetInt64()).UtcDateTime,
                CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(arr[6].GetInt64()).UtcDateTime,
                Open      = decimal.Parse(arr[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                High      = decimal.Parse(arr[2].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                Low       = decimal.Parse(arr[3].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                Close     = decimal.Parse(arr[4].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                Volume    = decimal.Parse(arr[5].GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            };
        }).ToList();

        _cache.Set(cacheKey, candles, TimeSpan.FromSeconds(30));
        return candles;
    }
}
