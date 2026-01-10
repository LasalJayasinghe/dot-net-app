using System.Text;
using System.Text.Json;
using dotnetApp.Data;
using dotnetApp.Models.Dtos;

namespace dotnetApp;

public class StockService
{

    private readonly HttpClient _httpClient;
    private readonly ILogger<StockService> _logger;
    private readonly AppDbContext _db;

    public StockService(HttpClient httpClient, ILogger<StockService> logger, AppDbContext db)
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
    }

    public async Task<StockDataResponseDto?> GetStockDataAsync(string symbol)
    {
        try
        {
            var url = "https://www.cse.lk/api/companyInfoSummery";

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("symbol", symbol)
            });

            // Set headers
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("accept", "application/json, text/plain, */*");
            request.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            request.Content = formData;

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var data = JsonSerializer.Deserialize<StockDataResponseDto>(content, options);
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
            var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://www.cse.lk/api/tradeSummary"
                    );

            request.Headers.Add("Accept", "application/json, text/plain, */*");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            request.Headers.Add("Origin", "https://www.cse.lk");
            request.Headers.Add("Referer", "https://www.cse.lk/equity/trade-summary");

            request.Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var existingStocks = _db.Stocks.ToDictionary(s => s.Symbol, s => s);

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var stockItems = JsonSerializer.Deserialize<TradeSummaryResponseDto>(content, options);
            if (stockItems == null)
            {
                _logger.LogWarning("No trading summary data received.");
                return;
            }

            foreach (var item in stockItems.ReqTradeSummery)
            {
                if (existingStocks.TryGetValue(item.Symbol, out var stock))
                {
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
                    stock = new Stocks
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
                    };
                    _db.Stocks.Add(stock);
                }
            }

            await _db.SaveChangesAsync();
            Console.WriteLine("Trading Summary Data updated at " + DateTime.Now);
            _logger.LogInformation("Trading Summary Data updated at " + DateTime.Now);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
        }
    }
}
