public interface IAiMarketSummaryService
{
    /// <summary>
    /// Generates a rule-based, trader-friendly market summary.
    /// Structured for future replacement with a real AI provider without changing the interface.
    /// </summary>
    string GenerateSummary(string symbol, decimal price, decimal ema9, decimal ema21, decimal rsi, string condition);
}
