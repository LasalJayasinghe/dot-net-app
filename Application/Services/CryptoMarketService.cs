public class CryptoMarketService : ICryptoMarketService
{
    /// <summary>
    /// Evaluates the EMA/RSI strategy for a given symbol and candle set.
    /// Reuses the existing EmaIndicator and RsiIndicator classes.
    /// </summary>
    public StrategySnapshot GetStrategySnapshot(string symbol, List<Candle> candles)
    {
        if (candles.Count < 30)
        {
            return new StrategySnapshot
            {
                Symbol = symbol,
                Signal = "WAIT",
                Reason = "Insufficient candle data (need at least 30 candles)",
                MarketCondition = "Neutral"
            };
        }

        var closes = candles.Select(c => c.Close).ToList();

        // Reuse existing indicator classes — do NOT duplicate logic
        var ema9Indicator  = new EmaIndicator(9);
        var ema21Indicator = new EmaIndicator(21);
        var rsiIndicator   = new RsiIndicator(14);

        var ema9  = ema9Indicator.Calculate(closes);
        var ema21 = ema21Indicator.Calculate(closes);
        var rsi   = rsiIndicator.Calculate(closes);
        var price = closes.Last();

        var condition = GetMarketCondition(ema9, ema21, rsi);

        string signal = "WAIT";
        string reason = "No clear directional signal";

        if (ema9 > ema21 && rsi is >= 30 and <= 65)
        {
            signal = "BUY";
            reason = "EMA 9 above EMA 21 with RSI in healthy bullish range";
        }
        else if (ema9 > ema21 && rsi < 30)
        {
            signal = "BUY";
            reason = "EMA crossover confirmed with RSI in oversold territory";
        }
        else if (ema9 < ema21 && rsi > 70)
        {
            signal = "SELL";
            reason = "EMA bearish crossover with RSI in overbought territory";
        }
        else if (ema9 < ema21)
        {
            signal = "SELL";
            reason = "EMA 9 crossed below EMA 21 — bearish momentum";
        }
        else if (rsi > 72)
        {
            signal = "SELL";
            reason = "RSI approaching overbought — potential reversal zone";
        }

        return new StrategySnapshot
        {
            Symbol          = symbol,
            Ema9            = Math.Round(ema9, 2),
            Ema21           = Math.Round(ema21, 2),
            Rsi             = Math.Round(rsi, 2),
            CurrentPrice    = price,
            Signal          = signal,
            Reason          = reason,
            MarketCondition = condition,
            Confidence      = GetConfidenceScore(ema9, ema21, rsi, signal),
            EvaluatedAt     = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Scans multiple symbols and returns ranked trading opportunities.
    /// </summary>
    public IEnumerable<ScannerResult> GetScannerResults(Dictionary<string, List<Candle>> allCandles)
    {
        var results = new List<ScannerResult>();

        foreach (var (symbol, candles) in allCandles)
        {
            if (candles.Count < 30)
                continue;

            var snapshot = GetStrategySnapshot(symbol, candles);

            results.Add(new ScannerResult
            {
                Symbol          = symbol,
                Signal          = snapshot.Signal,
                Confidence      = snapshot.Confidence,
                Price           = snapshot.CurrentPrice,
                MarketCondition = snapshot.MarketCondition,
                Rsi             = snapshot.Rsi,
                Ema9            = snapshot.Ema9,
                Ema21           = snapshot.Ema21
            });
        }

        // Rank by confidence descending, BUY/SELL before WAIT
        return results
            .OrderBy(r => r.Signal == "WAIT" ? 1 : 0)
            .ThenByDescending(r => r.Confidence);
    }

    public string GetMarketCondition(decimal ema9, decimal ema21, decimal rsi)
    {
        if (ema9 > ema21 && rsi > 50) return "Bullish";
        if (ema9 < ema21 && rsi < 50) return "Bearish";
        return "Neutral";
    }

    public int GetConfidenceScore(decimal ema9, decimal ema21, decimal rsi, string signalType)
    {
        int score = 40;
        var emaDiffPct = ema21 > 0 ? Math.Abs((ema9 - ema21) / ema21 * 100) : 0;

        switch (signalType)
        {
            case "BUY":
                if (ema9 > ema21)           score += 20;
                if (rsi is >= 35 and <= 55) score += 18;
                if (rsi < 30)               score += 22;
                score += (int)Math.Min(emaDiffPct * 8, 15);
                break;

            case "SELL":
                if (ema9 < ema21)  score += 20;
                if (rsi > 70)      score += 22;
                if (rsi > 80)      score += 10;
                score += (int)Math.Min(emaDiffPct * 8, 15);
                break;

            default: // WAIT
                score = 30 + (int)Math.Min(emaDiffPct * 2, 20);
                break;
        }

        return Math.Clamp(score, 10, 99);
    }
}
