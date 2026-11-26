using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Application.Services;

/// <summary>What an alert symbol refers to.</summary>
public enum AlertAssetType { Stock, Index, Crypto, Fx }

/// <summary>Display info for a non-stock alert symbol.</summary>
public sealed record MarketSymbolInfo(string Symbol, string Label, AlertAssetType Type, string? Unit, int Decimals);

/// <summary>
/// Current price for any alert symbol, from data the app already keeps up to date:
///   CSE stocks  → Stocks table (AlertJob, every 2 min while open)
///   ASPI / SNPSL20 → CseMarketIndices (AlertJob, every 5 min while open)
///   BTCUSDT / SOLUSDT → BinanceService live WebSocket tickers
///   USDTLKR → CurrencyExchangeRates (Binance P2P rate, ExchangeRateJob)
/// Non-stock symbols contain no dot, so they can never collide with CSE codes like "COMB.N0000".
/// </summary>
public class MarketPriceResolver
{
    public const string Aspi = "ASPI";
    public const string SnpSl20 = "SNPSL20";
    public const string BtcUsdt = "BTCUSDT";
    public const string SolUsdt = "SOLUSDT";
    public const string UsdtLkr = "USDTLKR";

    private static readonly Dictionary<string, MarketSymbolInfo> Markets = new(StringComparer.OrdinalIgnoreCase)
    {
        [Aspi] = new(Aspi, "ASPI Index", AlertAssetType.Index, "pts", 2),
        [SnpSl20] = new(SnpSl20, "S&P SL20 Index", AlertAssetType.Index, "pts", 2),
        [BtcUsdt] = new(BtcUsdt, "BTC/USDT", AlertAssetType.Crypto, "USDT", 2),
        [SolUsdt] = new(SolUsdt, "SOL/USDT", AlertAssetType.Crypto, "USDT", 4),
        // The rest of BinanceService.TrackedSymbols, so every pair on the trading desk can be alerted.
        ["ETHUSDT"] = new("ETHUSDT", "ETH/USDT", AlertAssetType.Crypto, "USDT", 2),
        ["BNBUSDT"] = new("BNBUSDT", "BNB/USDT", AlertAssetType.Crypto, "USDT", 2),
        ["XRPUSDT"] = new("XRPUSDT", "XRP/USDT", AlertAssetType.Crypto, "USDT", 4),
        ["ADAUSDT"] = new("ADAUSDT", "ADA/USDT", AlertAssetType.Crypto, "USDT", 4),
        ["DOGEUSDT"] = new("DOGEUSDT", "DOGE/USDT", AlertAssetType.Crypto, "USDT", 5),
        [UsdtLkr] = new(UsdtLkr, "USDT/LKR", AlertAssetType.Fx, "LKR", 2),
    };

    private readonly AppDbContext _db;
    private readonly BinanceService _binance;

    public MarketPriceResolver(AppDbContext db, BinanceService binance)
    {
        _db = db;
        _binance = binance;
    }

    public static IReadOnlyCollection<MarketSymbolInfo> MarketSymbols => Markets.Values;

    public static bool IsMarketSymbol(string symbol) => Markets.ContainsKey(symbol.Trim());

    public static AlertAssetType AssetTypeOf(string symbol) =>
        Markets.TryGetValue(symbol.Trim(), out var m) ? m.Type : AlertAssetType.Stock;

    /// <summary>Canonical symbol: market symbols upper-cased, CSE codes normalised ("COMB" → "COMB.N0000").</summary>
    public static string Normalize(string symbol)
    {
        var s = symbol.Trim().ToUpperInvariant();
        return IsMarketSymbol(s) ? s : CseSymbols.ToLookup(s);
    }

    /// <summary>"ASPI Index" / "BTC/USDT" / the CSE code for stocks.</summary>
    public static string LabelOf(string symbol) =>
        Markets.TryGetValue(symbol.Trim(), out var m) ? m.Label : symbol;

    /// <summary>"95,000.00 USDT", "21,743.33 pts", "Rs 151.75".</summary>
    public static string FormatPrice(string symbol, decimal price)
    {
        if (!Markets.TryGetValue(symbol.Trim(), out var m))
            return $"Rs {price:N2}";
        var formatted = price.ToString("N" + m.Decimals);
        return m.Unit == "LKR" ? $"Rs {formatted}" : $"{formatted} {m.Unit}";
    }

    /// <summary>True for a market symbol, or a CSE security present in the Stocks table.</summary>
    public async Task<bool> IsSupportedAsync(string symbol, CancellationToken ct = default)
    {
        var s = Normalize(symbol);
        return IsMarketSymbol(s) || await _db.Stocks.AsNoTracking().AnyAsync(x => x.Symbol == s, ct);
    }

    /// <summary>Current prices keyed by the symbol exactly as passed in. Unknown/unavailable symbols are omitted.</summary>
    public async Task<Dictionary<string, decimal>> GetPricesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var requested = symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requested.Count == 0) return result;

        // CSE stocks: one query (stored as full codes; requests may be "COMB" or "COMB.N0000").
        var stockRequests = requested.Where(s => !IsMarketSymbol(s)).ToList();
        if (stockRequests.Count > 0)
        {
            var lookups = stockRequests.Select(CseSymbols.ToLookup).Distinct().ToList();
            var prices = await _db.Stocks.AsNoTracking()
                .Where(x => lookups.Contains(x.Symbol))
                .Select(x => new { x.Symbol, x.Price, x.ClosingPrice })
                .ToListAsync(ct);
            var byCode = prices.ToDictionary(
                p => p.Symbol, p => p.Price > 0 ? p.Price : p.ClosingPrice, StringComparer.OrdinalIgnoreCase);

            foreach (var s in stockRequests)
                if (byCode.TryGetValue(CseSymbols.ToLookup(s), out var price) && price > 0)
                    result[s] = price;
        }

        // Indices
        var indexRequests = requested.Where(s => AssetTypeOf(s) == AlertAssetType.Index).ToList();
        if (indexRequests.Count > 0)
        {
            var indices = await _db.MarketIndices.AsNoTracking().ToListAsync(ct);
            foreach (var s in indexRequests)
            {
                var type = s.Equals(Aspi, StringComparison.OrdinalIgnoreCase) ? global::MarketIndexType.ASPI : global::MarketIndexType.SNP;
                var value = indices.FirstOrDefault(i => i.IndexType == type)?.Value ?? 0;
                if (value > 0) result[s] = value;
            }
        }

        // Crypto (live, in memory)
        foreach (var s in requested.Where(s => AssetTypeOf(s) == AlertAssetType.Crypto))
        {
            var key = s.ToUpperInvariant();
            var price = _binance.GetTicker(key)?.LastPrice ?? _binance.Prices.GetValueOrDefault(key, 0);
            if (price > 0) result[s] = price;
        }

        // USDT → LKR (Binance P2P)
        if (requested.Any(s => s.Equals(UsdtLkr, StringComparison.OrdinalIgnoreCase)))
        {
            var rate = await _db.CurrencyExchangeRates.AsNoTracking()
                .Where(r => r.FromCurrency == "USDT" && r.ToCurrency == "LKR")
                .Select(r => (decimal?)r.Rate)
                .FirstOrDefaultAsync(ct);
            if (rate is > 0)
                foreach (var s in requested.Where(s => s.Equals(UsdtLkr, StringComparison.OrdinalIgnoreCase)))
                    result[s] = rate.Value;
        }

        return result;
    }
}
