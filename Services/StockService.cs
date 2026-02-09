using System.Text;
using System.Text.Json;
using dotnetApp.Data;
using dotnetApp.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace dotnetApp;

public class StockService
{

    private readonly HttpClient _httpClient;
    private readonly ILogger<StockService> _logger;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public StockService(HttpClient httpClient, ILogger<StockService> logger, AppDbContext db, IMemoryCache cache
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
        _cache = cache;
    }

    public async Task<StockDataResponseDto?> GetStockDataAsync(string symbol)
    {
        try
        {
            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("symbol", symbol)
            });

            var response = await _httpClient.PostAsync(
                "https://www.cse.lk/api/companyInfoSummery",
                formData
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var data = JsonSerializer.Deserialize<StockDataResponseDto>(content, options);

            var existingStock = await _db.Stocks
                .FirstAsync(s => s.Symbol == symbol);

            if (data != null && existingStock != null)
            {
                existingStock.Price = data.ReqSymbolInfo.LastTradedPrice;
                existingStock.PreviousClose = data.ReqSymbolInfo.PreviousClose;
                existingStock.High = data.ReqSymbolInfo.High;
                existingStock.Low = data.ReqSymbolInfo.Low;
                existingStock.ClosingPrice = data.ReqSymbolInfo.ClosingPrice;
                existingStock.PercentageChange = data.ReqSymbolInfo.PercentageChange;
                existingStock.Change = data.ReqSymbolInfo.Change;

                await _db.SaveChangesAsync();
            }
            return data;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task GetTradingSummaryAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("https://www.cse.lk/api/tradeSummary", null);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var stockItems = JsonSerializer.Deserialize<TradeSummaryResponseDto>(content, options);
            if (stockItems == null)
            {
                _logger.LogWarning("No trading summary data received.");
                return;
            }

            // Get all symbols in the response
            var symbols = stockItems.ReqTradeSummery.Select(s => s.Symbol).ToList();

            // Fetch existing stocks in one query
            var existingStocks = await _db.Stocks
                .Where(s => symbols.Contains(s.Symbol))
                .ToDictionaryAsync(s => s.Symbol);

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
                        _db.Stocks.Add(new Stocks
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
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation("Trading Summary Data updated at {Time}", DateTime.Now);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Request error while fetching trading summary");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating trading summary");
        }
    }


    public async Task<List<Stocks>> GetAllStockNamesAsync()
    {
        const string cacheKey = "AllStockNames";

        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await _db.Stocks
                    .AsNoTracking()
                    .OrderBy(s => s.Name)
                    .Select(s => new Stocks
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Symbol = s.Symbol
                    })
                    .ToListAsync();
        });

        return result ?? new List<Stocks>();

    }

    public async Task<MarketStatusDto?> GetMarketStatus()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.cse.lk/api/marketStatus");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var marketStatus = JsonSerializer.Deserialize<MarketStatusDto>(content, options);
            return marketStatus;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<StockTopDto>?> GetTopGainers()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.cse.lk/api/topGainers");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var topGainers = JsonSerializer.Deserialize<List<StockTopDto>>(content, options);
            return topGainers ?? new List<StockTopDto>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<StockTopDto>?> GetTopLooses()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.cse.lk/api/topLooses");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var topLooses = JsonSerializer.Deserialize<List<StockTopDto>>(content, options);
            return topLooses ?? new List<StockTopDto>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<StockIndicesDto>?> GetASPIData()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.cse.lk/api/aspiData");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var aspiData = JsonSerializer.Deserialize<List<StockIndicesDto>>(content, options);
            return aspiData ?? new List<StockIndicesDto>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<StockIndicesDto>?> GetSnpData()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.cse.lk/api/snpData");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var snpData = JsonSerializer.Deserialize<List<StockIndicesDto>>(content, options);
            return snpData ?? new List<StockIndicesDto>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }


}
