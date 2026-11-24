using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Application.Interface;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services.MarketProviders;

/// <summary>
/// Reads quotes from Yahoo Finance's public chart endpoint (no API key).
/// Note: this endpoint is unofficial — it can rate-limit or change without notice, which is why it
/// sits behind <see cref="IGlobalMarketProvider"/> and the service keeps the last good values.
/// </summary>
public class YahooFinanceMarketProvider : IGlobalMarketProvider
{
    private const int MaxConcurrentRequests = 4;

    private readonly HttpClient _httpClient;
    private readonly ILogger<YahooFinanceMarketProvider> _logger;

    public string ProviderName => "Yahoo Finance";

    public YahooFinanceMarketProvider(HttpClient httpClient, ILogger<YahooFinanceMarketProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProviderMarketQuote>> FetchQuotesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentBag<ProviderMarketQuote>();
        using var gate = new SemaphoreSlim(MaxConcurrentRequests);

        var tasks = symbols.Distinct().Select(async symbol =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var quote = await FetchOneAsync(symbol, cancellationToken);
                if (quote != null) results.Add(quote);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yahoo quote fetch failed for {Symbol}", symbol);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<ProviderMarketQuote?> FetchOneAsync(string symbol, CancellationToken ct)
    {
        // range=1d → chartPreviousClose is the previous session's close.
        var url = $"v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=1d";
        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Yahoo returned {Status} for {Symbol}", (int)response.StatusCode, symbol);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var resultArr) ||
            resultArr.ValueKind != JsonValueKind.Array ||
            resultArr.GetArrayLength() == 0 ||
            !resultArr[0].TryGetProperty("meta", out var meta))
        {
            return null;
        }

        var price = ReadDecimal(meta, "regularMarketPrice");
        var previousClose = ReadDecimal(meta, "chartPreviousClose") ?? ReadDecimal(meta, "previousClose");
        if (price is null || previousClose is null || previousClose <= 0) return null;

        DateTime? marketTime = null;
        if (ReadLong(meta, "regularMarketTime") is { } t)
            marketTime = DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime;

        var isOpen = false;
        if (meta.TryGetProperty("currentTradingPeriod", out var period) &&
            period.TryGetProperty("regular", out var regular) &&
            ReadLong(regular, "start") is { } start &&
            ReadLong(regular, "end") is { } end)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            isOpen = now >= start && now < end;
        }

        return new ProviderMarketQuote
        {
            Symbol = symbol,
            Currency = meta.TryGetProperty("currency", out var cur) && cur.ValueKind == JsonValueKind.String
                ? cur.GetString() ?? ""
                : "",
            Price = price.Value,
            PreviousClose = previousClose.Value,
            ChangePercent = ReadDecimal(meta, "regularMarketChangePercent"),
            IsOpen = isOpen,
            MarketTime = marketTime,
        };
    }

    private static decimal? ReadDecimal(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)
            ? d
            : null;

    private static long? ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var l)
            ? l
            : null;
}
