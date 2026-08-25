using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services;

public class BinanceP2pService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BinanceP2pService> _logger;

    private const string P2pApiUrl = "https://p2p.binance.com/bapi/c2c/v2/friendly/c2c/adv/search";

    public BinanceP2pService(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        ILogger<BinanceP2pService> logger)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    /// <summary>
    /// Fetches current USDT/LKR P2P rate from Binance and updates DB exchange rates.
    /// </summary>
    public async Task<(decimal usdtToLkr, decimal lkrToUsdt)> FetchAndUpdateUsdtLkrRateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching Binance P2P USDT/LKR exchange rate...");

        try
        {
            var payload = new
            {
                asset = "USDT",
                fiat = "LKR",
                merchantCheck = false,
                page = 1,
                rows = 10,
                tradeType = "BUY"
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(P2pApiUrl, jsonContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Binance P2P API returned HTTP status {StatusCode}", response.StatusCode);
                return await GetStoredRatesAsync();
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            if (!doc.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Binance P2P response 'data' property missing or invalid.");
                return await GetStoredRatesAsync();
            }

            var prices = new List<decimal>();
            foreach (var item in dataElement.EnumerateArray())
            {
                if (item.TryGetProperty("adv", out var adv) && adv.TryGetProperty("price", out var priceProp))
                {
                    var priceStr = priceProp.GetString();
                    if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price > 0)
                    {
                        prices.Add(price);
                    }
                }
            }

            if (!prices.Any())
            {
                _logger.LogWarning("No valid P2P advertiser prices found in Binance response.");
                return await GetStoredRatesAsync();
            }

            // Take average of top advertiser rates for accuracy
            decimal averageUsdtToLkr = Math.Round(prices.Take(5).Average(), 2);
            decimal lkrToUsdt = averageUsdtToLkr > 0 ? Math.Round(1m / averageUsdtToLkr, 6) : 0.0033m;

            _logger.LogInformation("Binance P2P Rate Fetched: 1 USDT = {UsdtToLkr} LKR (1 LKR = {LkrToUsdt} USDT)", averageUsdtToLkr, lkrToUsdt);

            // Update Database
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var usdtToLkrEntity = await db.CurrencyExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == "USDT" && r.ToCurrency == "LKR", ct);
            if (usdtToLkrEntity == null)
            {
                db.CurrencyExchangeRates.Add(new CurrencyExchangeRate { FromCurrency = "USDT", ToCurrency = "LKR", Rate = averageUsdtToLkr, LastUpdated = DateTime.UtcNow });
            }
            else
            {
                usdtToLkrEntity.Rate = averageUsdtToLkr;
                usdtToLkrEntity.LastUpdated = DateTime.UtcNow;
            }

            var lkrToUsdtEntity = await db.CurrencyExchangeRates.FirstOrDefaultAsync(r => r.FromCurrency == "LKR" && r.ToCurrency == "USDT", ct);
            if (lkrToUsdtEntity == null)
            {
                db.CurrencyExchangeRates.Add(new CurrencyExchangeRate { FromCurrency = "LKR", ToCurrency = "USDT", Rate = lkrToUsdt, LastUpdated = DateTime.UtcNow });
            }
            else
            {
                lkrToUsdtEntity.Rate = lkrToUsdt;
                lkrToUsdtEntity.LastUpdated = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            return (averageUsdtToLkr, lkrToUsdt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating exchange rates from Binance P2P API");
            return await GetStoredRatesAsync();
        }
    }

    private async Task<(decimal usdtToLkr, decimal lkrToUsdt)> GetStoredRatesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usdtToLkr = await db.CurrencyExchangeRates.AsNoTracking().FirstOrDefaultAsync(r => r.FromCurrency == "USDT" && r.ToCurrency == "LKR");
        var lkrToUsdt = await db.CurrencyExchangeRates.AsNoTracking().FirstOrDefaultAsync(r => r.FromCurrency == "LKR" && r.ToCurrency == "USDT");

        return (usdtToLkr?.Rate ?? 300m, lkrToUsdt?.Rate ?? 0.0033m);
    }
}
