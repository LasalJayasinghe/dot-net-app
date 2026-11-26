public class WatchlistItem
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Symbol { get; set; } = null!;

    /// <summary>"Cse" (stock code, e.g. COMB) or "Crypto" (Binance pair, e.g. BTCUSDT).</summary>
    public string Market { get; set; } = WatchlistMarkets.Cse;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}

public static class WatchlistMarkets
{
    public const string Cse = "Cse";
    public const string Crypto = "Crypto";
}
