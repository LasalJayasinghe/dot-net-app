using System.Text.Json;

namespace dotnetApp;

public class StockService
{

    private readonly HttpClient _httpClient;
    private readonly ILogger<StockService> _logger;

    public StockService(HttpClient httpClient, ILogger<StockService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<object?> GetStockDataAsync(string symbol)
    {
        try
        {
            _logger.LogInformation($"Fetching stock data for {symbol}");
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

            var data = JsonSerializer.Deserialize<object>(content, options);

            return data;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            return null;
        }
    }
}
