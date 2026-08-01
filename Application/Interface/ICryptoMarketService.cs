public interface ICryptoMarketService
{
    StrategySnapshot GetStrategySnapshot(string symbol, List<Candle> candles);
    IEnumerable<ScannerResult> GetScannerResults(Dictionary<string, List<Candle>> allCandles);
    string GetMarketCondition(decimal ema9, decimal ema21, decimal rsi);
    int GetConfidenceScore(decimal ema9, decimal ema21, decimal rsi, string signalType);
}
