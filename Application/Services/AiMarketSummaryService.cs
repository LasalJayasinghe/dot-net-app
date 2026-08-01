using System.Text;

public class AiMarketSummaryService : IAiMarketSummaryService
{
    /// <summary>
    /// Generates a rule-based trader-friendly market analysis paragraph.
    /// No external AI call — structured so the implementation can be swapped
    /// for a real LLM provider (e.g. Gemini, OpenAI) without changing the interface.
    /// </summary>
    public string GenerateSummary(
        string symbol,
        decimal price,
        decimal ema9,
        decimal ema21,
        decimal rsi,
        string condition)
    {
        var ticker = symbol.Replace("USDT", "").Replace("usdt", "");
        var sb = new StringBuilder();

        // ── Opening line ────────────────────────────────────────────────────
        if (ema9 > ema21)
        {
            sb.AppendLine($"{ticker} is currently trading above EMA 21, indicating that short-term " +
                          $"momentum remains bullish.");
        }
        else
        {
            sb.AppendLine($"{ticker} is currently trading below EMA 21, suggesting that bearish " +
                          $"pressure is in control of the near-term trend.");
        }

        // ── EMA spread commentary ────────────────────────────────────────────
        var emaDiffPct = ema21 > 0 ? Math.Abs((ema9 - ema21) / ema21 * 100) : 0;
        if (emaDiffPct < 0.2m)
        {
            sb.AppendLine($"EMA 9 and EMA 21 are converging tightly, signaling potential indecision " +
                          $"and a possible trend change ahead.");
        }
        else if (emaDiffPct > 1.0m)
        {
            sb.AppendLine($"The spread between EMA 9 and EMA 21 is widening, reinforcing the " +
                          $"{(ema9 > ema21 ? "upward" : "downward")} momentum.");
        }

        // ── RSI analysis ─────────────────────────────────────────────────────
        sb.AppendLine();
        if (rsi > 70)
        {
            sb.AppendLine($"RSI at {rsi:F0} has entered overbought territory. " +
                          $"Caution is advised — momentum may stall or reverse.");
        }
        else if (rsi < 30)
        {
            sb.AppendLine($"RSI at {rsi:F0} is deeply oversold, " +
                          $"presenting a potential mean-reversion opportunity for risk-tolerant traders.");
        }
        else if (rsi is >= 50 and <= 65)
        {
            sb.AppendLine($"RSI at {rsi:F0} sits in a healthy bullish zone, " +
                          $"supporting continued upward momentum without immediate overbought risk.");
        }
        else
        {
            sb.AppendLine($"RSI at {rsi:F0} remains in neutral territory, " +
                          $"offering no strong directional bias on its own.");
        }

        // ── Possible scenarios ────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("Possible scenarios:");

        switch (condition)
        {
            case "Bullish":
                sb.AppendLine($"  • Continuation breakout if {ticker} holds above EMA 21 on a retest");
                sb.AppendLine($"  • Short-term pullback to EMA 9 ({ema9:F2}) before resuming the uptrend");
                if (rsi > 65)
                    sb.AppendLine($"  • RSI cool-down from current elevated levels before the next leg up");
                break;

            case "Bearish":
                sb.AppendLine($"  • Continued downside if {ticker} fails to reclaim EMA 21 ({ema21:F2})");
                sb.AppendLine($"  • Oversold relief rally possible as short-sellers take profits");
                sb.AppendLine($"  • Watch for a bearish retest of EMA 9 as resistance");
                break;

            default: // Neutral
                sb.AppendLine($"  • Range-bound consolidation between EMA 9 ({ema9:F2}) and EMA 21 ({ema21:F2})");
                sb.AppendLine($"  • A decisive close above or below either EMA level will signal the next direction");
                break;
        }

        return sb.ToString().Trim();
    }
}
