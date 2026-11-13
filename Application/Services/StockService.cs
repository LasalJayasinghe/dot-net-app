using System.Text.Json;
using dotnetApp.Application.Dtos;
using dotnetApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace dotnetApp;

/// <summary>CSE symbol helpers. CSE APIs expect the full security code, e.g. "COMB.N0000".</summary>
public static class CseSymbols
{
    /// <summary>"comb" / "COMB" → "COMB.N0000"; full codes ("COMB.X0000") are kept as-is.</summary>
    public static string ToLookup(string symbol)
    {
        var s = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        return s.Length == 0 || s.Contains('.') ? s : $"{s}.N0000";
    }

    /// <summary>"COMB.N0000" → "COMB".</summary>
    public static string BaseOf(string symbol)
    {
        var s = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        var dot = s.IndexOf('.');
        return dot < 0 ? s : s[..dot];
    }
}

public class StockService
{
    private const int MaxAttempts = 2;

    private readonly HttpClient _httpClient;
    private readonly ILogger<StockService> _logger;
    private readonly IMemoryCache _cache;
    private readonly StockRepository _stockRepository;

    public StockService(HttpClient httpClient, ILogger<StockService> logger, StockRepository stockRepository, IMemoryCache cache
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _stockRepository = stockRepository;
        _cache = cache;
    }

    /// <summary>
    /// POST to the CSE API with one retry on network errors, timeouts and 5xx responses.
    /// The client timeout is set in Program.cs so a slow cse.lk can't stall the polling jobs.
    /// </summary>
    private async Task<HttpResponseMessage> PostAsync(string url, Func<HttpContent?>? content = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsync(url, content?.Invoke());
                if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
                {
                    _logger.LogWarning("CSE {Url} returned {Status}; retrying.", url, (int)response.StatusCode);
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                    continue;
                }
                return response;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("CSE {Url} failed ({Error}); retrying.", url, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    public async Task<StockDataResponseDto?> GetStockDataAsync(string symbol)
    {
        symbol = CseSymbols.ToLookup(symbol);
        try
        {
            var response = await PostAsync(
                "https://www.cse.lk/api/companyInfoSummery",
                () => new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("symbol", symbol) })
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var data = JsonSerializer.Deserialize<StockDataResponseDto>(content, options);

            var existingStock = await _stockRepository.GetBySymbolAsync(symbol);

            if (data != null && data.ReqSymbolInfo != null && !string.IsNullOrEmpty(data.ReqSymbolInfo.Symbol))
            {
                if (existingStock != null)
                {
                    existingStock.Price = data.ReqSymbolInfo.LastTradedPrice;
                    existingStock.PreviousClose = data.ReqSymbolInfo.PreviousClose;
                    existingStock.High = data.ReqSymbolInfo.High;
                    existingStock.Low = data.ReqSymbolInfo.Low;
                    existingStock.ClosingPrice = data.ReqSymbolInfo.ClosingPrice;
                    existingStock.PercentageChange = data.ReqSymbolInfo.PercentageChange;
                    existingStock.Change = data.ReqSymbolInfo.Change;

                    await _stockRepository.SaveChangesAsync();
                }
                return data;
            }
            return null;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task GetTradingSummaryAsync()
    {
        try
        {
            var response = await PostAsync("https://www.cse.lk/api/tradeSummary");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var stockItems = JsonSerializer.Deserialize<TradeSummaryResponseDto>(content, options);
            if (stockItems == null)
            {
                _logger.LogWarning("No trading summary data received.");
                return;
            }

            // CSE's internal ids are needed for chart history lookups.
            _cache.Set(StockIdsCacheKey,
                stockItems.ReqTradeSummery
                    .Where(i => i.Id > 0 && !string.IsNullOrEmpty(i.Symbol))
                    .GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromDays(1));

            // Get all symbols in the response
            var symbols = stockItems.ReqTradeSummery.Select(s => s.Symbol).ToList();

            // Fetch existing stocks in one query
            var existingStocksList = await _stockRepository.GetBySymbolsAsync(symbols);
            var existingStocks = existingStocksList.ToDictionary(s => s.Symbol, s => s);

            int batchSize = 50;
            for (int i = 0; i < stockItems.ReqTradeSummery.Count; i += batchSize)
            {
                var batch = stockItems.ReqTradeSummery.Skip(i).Take(batchSize);

                foreach (var item in batch)
                {
                    if (existingStocks.TryGetValue(item.Symbol, out var stock))
                    {
                        // Update existing stock
                        stock.Name = item.Name;
                        stock.Price = item.Price;
                        stock.PreviousClose = item.PreviousClose;
                        stock.High = item.High;
                        stock.Low = item.Low;
                        stock.ClosingPrice = item.ClosingPrice;
                        stock.PercentageChange = item.PercentageChange;
                        stock.Change = item.Change;
                    }
                    else
                    {
                        // Insert new stock
                        await _stockRepository.AddAsync(new Stock
                        {
                            Symbol = item.Symbol,
                            Name = item.Name,
                            Price = item.Price,
                            PreviousClose = item.PreviousClose,
                            High = item.High,
                            Low = item.Low,
                            ClosingPrice = item.ClosingPrice,
                            PercentageChange = item.PercentageChange,
                            Change = item.Change
                        });
                    }
                }

                // Save batch
                await _stockRepository.SaveChangesAsync();
            }

            // _logger.LogInformation("Trading Summary Data updated at {Time}", DateTime.Now);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogError(ex, "Request error while fetching trading summary");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating trading summary");
        }
    }


    private const string StockIdsCacheKey = "cse-stock-ids";

    /// <summary>
    /// Real price history from cse.lk (companyChartDataByStock).
    /// range: 1D (intraday trades), 1W, 1M, 3M, 1Y, ALL (daily closes; CSE serves about one year).
    /// CSE daily points carry high/low/close/volume but no open, so open = previous close.
    /// </summary>
    public async Task<List<PriceHistoryPointDto>?> GetPriceHistoryAsync(string symbol, string range)
    {
        symbol = CseSymbols.ToLookup(symbol);
        range = (range ?? "1M").ToUpperInvariant();
        var intraday = range == "1D";

        var cacheKey = $"cse-history:{symbol}:{(intraday ? "1D" : "daily")}";
        if (!_cache.TryGetValue(cacheKey, out List<PriceHistoryPointDto>? points) || points is null)
        {
            var stockId = await GetCseStockIdAsync(symbol);
            if (stockId is null) return null;

            try
            {
                var response = await PostAsync("https://www.cse.lk/api/companyChartDataByStock",
                    () => new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("stockId", stockId.Value.ToString()),
                        new KeyValuePair<string, string>("period", intraday ? "1" : "5"),
                    }));
                response.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                points = new List<PriceHistoryPointDto>();
                if (doc.RootElement.TryGetProperty("chartData", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    decimal? prevClose = null;
                    foreach (var p in arr.EnumerateArray())
                    {
                        var close = Dec(p, "p");
                        if (close <= 0 || !p.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number) continue;

                        var open = prevClose ?? close;
                        // Intraday h/l are the running day high/low, so draw each trade as a point.
                        var high = intraday ? Math.Max(open, close) : Math.Max(Dec(p, "h"), Math.Max(open, close));
                        var low = intraday ? Math.Min(open, close) : MinPositive(Dec(p, "l"), Math.Min(open, close));
                        points.Add(new PriceHistoryPointDto(t.GetInt64() / 1000, open, high, low, close, Dec(p, "q")));
                        prevClose = close;
                    }
                }

                _cache.Set(cacheKey, points, intraday ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(30));
            }
            catch (Exception ex) when (IsTransient(ex) || ex is JsonException)
            {
                _logger.LogWarning(ex, "Failed to fetch CSE price history for {Symbol}", symbol);
                return null;
            }
        }

        if (intraday) return points;

        var days = range switch { "1W" => 7, "1M" => 31, "3M" => 92, _ => 0 };
        if (days == 0) return points;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
        return points.Where(p => p.Time >= cutoff).ToList();

        static decimal Dec(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
        static decimal MinPositive(decimal a, decimal b) => a > 0 ? Math.Min(a, b) : b;
    }

    /// <summary>Symbol → CSE internal id, from the cached trade summary (refreshed by AlertJob).</summary>
    private async Task<int?> GetCseStockIdAsync(string symbol)
    {
        if (!_cache.TryGetValue(StockIdsCacheKey, out Dictionary<string, int>? ids) || ids is null)
        {
            await GetTradingSummaryAsync();
            _cache.TryGetValue(StockIdsCacheKey, out ids);
        }
        return ids != null && ids.TryGetValue(symbol, out var id) ? id : null;
    }

    public async Task<List<Stock>> GetAllStockNamesAsync()
    {
        const string cacheKey = "AllStockNames";

        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await _stockRepository.GetAllStockNamesAsync();
        });

        return result ?? new List<Stock>();
    }

    public async Task<List<Stock>> GetStockTickersAsync()
    {
        return await _stockRepository.GetStockTickersAsync();
    }

    public async Task<MarketStatusDto?> GetMarketStatus()
    {
        try
        {
            var response = await PostAsync("https://www.cse.lk/api/marketStatus");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var marketStatusDto = JsonSerializer.Deserialize<MarketStatusDto>(content, options);
            var marketStatus = new MarketStatus
            {
                IsOpen = marketStatusDto?.status != "Market Closed"
            };

            await _stockRepository.AddAsync(marketStatus);

            Console.WriteLine($"Market Status: {marketStatusDto?.status} at {DateTime.Now}");
            return marketStatusDto;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<StockTopDto>?> GetTopGainers()
    {
        try
        {
            var response = await PostAsync("https://www.cse.lk/api/topGainers");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var logContent = content.Length > 1000 ? content[..1000] + "..." : content;
            _logger.LogDebug("TopGainers response content: {ResponseContent}", logContent);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var topGainers = JsonSerializer.Deserialize<List<StockTopDto>>(content, options);
            return topGainers ?? new List<StockTopDto>();
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return new List<StockTopDto>();
        }
    }

    public async Task<List<StockTopDto>?> GetTopLooses()
    {
        try
        {
            var response = await PostAsync("https://www.cse.lk/api/topLooses");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var topLooses = JsonSerializer.Deserialize<List<StockTopDto>>(content, options);
            return topLooses ?? new List<StockTopDto>();
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return new List<StockTopDto>();
        }
    }

    public async Task<StockIndicesDto?> GetASPIData()
    {
        try
        {
            var response = await PostAsync("https://www.cse.lk/api/aspiData");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var aspiData = JsonSerializer.Deserialize<StockIndicesDto>(content, options)
                           ?? new StockIndicesDto();
            Console.WriteLine($"ASPI Data: {aspiData.IndexType} - Price: {aspiData.value}, Change: {aspiData.change}, Change%: {aspiData.percentage} at {DateTime.Now}");

            var marketIndex = new MarketIndices
            {
                IndexType = MarketIndexType.ASPI,
                Value = aspiData.value,
                HighValue = aspiData.highValue,
                LowValue = aspiData.lowValue,
                Change = aspiData.change,
                Percentage = aspiData.percentage
            };

            await _stockRepository.AddAsync(marketIndex);
            return aspiData;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<StockIndicesDto> GetSnpData()
    {
        try
        {
            var response = await PostAsync("https://www.cse.lk/api/snpData");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var snpData = JsonSerializer.Deserialize<StockIndicesDto>(content, options);
            if (snpData == null)
            {
                return new StockIndicesDto();
            }

            var marketIndex = new MarketIndices
            {
                IndexType = MarketIndexType.SNP,
                Value = snpData.value,
                HighValue = snpData.highValue,
                LowValue = snpData.lowValue,
                Change = snpData.change,
                Percentage = snpData.percentage
            };

            await _stockRepository.AddAsync(marketIndex);
            return snpData;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return new StockIndicesDto();
        }
    }

    public async Task<List<StockIntraDay>?> GetIntradayDataAsync()
    {
        return await _stockRepository.GetIntraDayValues();
    }

    public async Task<MarketStatus?> GetSavedMarketStatusAsync()
    {
        return await _stockRepository.GetMarketStatusAsync();
    }

    public async Task<StockIndicesDto> GetSavedAspiDataAsync()
    {
        var entity = await _stockRepository.GetMarketIndexAsync(global::MarketIndexType.ASPI);
        return ToStockIndicesDto(entity, dotnetApp.Application.Dtos.MarketIndexType.ASPI);
    }

    public async Task<StockIndicesDto> GetSavedSnpDataAsync()
    {
        var entity = await _stockRepository.GetMarketIndexAsync(global::MarketIndexType.SNP);
        return ToStockIndicesDto(entity, dotnetApp.Application.Dtos.MarketIndexType.SNP);
    }

    private static StockIndicesDto ToStockIndicesDto(MarketIndices? entity, dotnetApp.Application.Dtos.MarketIndexType fallbackType)
    {
        if (entity == null)
        {
            return new StockIndicesDto { IndexType = fallbackType };
        }

        return new StockIndicesDto
        {
            IndexType = (dotnetApp.Application.Dtos.MarketIndexType)(int)entity.IndexType,
            value = entity.Value,
            highValue = entity.HighValue,
            lowValue = entity.LowValue,
            change = entity.Change,
            percentage = entity.Percentage
        };
    }
}
