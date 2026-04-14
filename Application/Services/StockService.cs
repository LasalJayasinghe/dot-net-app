using System.Text.Json;
using dotnetApp.Application.Dtos;
using dotnetApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace dotnetApp;

public class StockService
{

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

            var existingStock = await _stockRepository.GetBySymbolAsync(symbol);

            if (data != null && existingStock != null)
            {
                existingStock.Price = data.ReqSymbolInfo.LastTradedPrice;
                existingStock.PreviousClose = data.ReqSymbolInfo.PreviousClose;
                existingStock.High = data.ReqSymbolInfo.High;
                existingStock.Low = data.ReqSymbolInfo.Low;
                existingStock.ClosingPrice = data.ReqSymbolInfo.ClosingPrice;
                existingStock.PercentageChange = data.ReqSymbolInfo.PercentageChange;
                existingStock.Change = data.ReqSymbolInfo.Change;

                await _stockRepository.SaveChangesAsync();
                return data;
            }
            return null;
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
            var existingStocks = await _stockRepository.GetBySymbolsAsync(symbols)
                .ContinueWith(t => t.Result.ToDictionary(s => s.Symbol, s => s));

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
                        await _stockRepository.AddAsync(new Stocks
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
            return await _stockRepository.GetAllStockNamesAsync();
        });

        return result ?? new List<Stocks>();
    }

    public async Task<MarketStatusDto?> GetMarketStatus()
    {
        try
        {
            var response = await _httpClient.PostAsync("https://www.cse.lk/api/marketStatus", null);
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

    public async Task<StockIndicesDto?> GetASPIData()
    {
        try
        {
            var response = await _httpClient.PostAsync("https://www.cse.lk/api/aspiData", null);
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
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<StockIndicesDto> GetSnpData()
    {
        try
        {
            var response = await _httpClient.PostAsync("https://www.cse.lk/api/snpData", null);
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
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<StockIntraDay>?> GetIntradayDataAsync()
    {
        return await _stockRepository.GetIntraDayValues();
    }
}
