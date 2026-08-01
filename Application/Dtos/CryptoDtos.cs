public class StrategySnapshot
{
    public string Symbol { get; set; } = "";
    public decimal Ema9 { get; set; }
    public decimal Ema21 { get; set; }
    public decimal Rsi { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Signal { get; set; } = "WAIT";
    public string Reason { get; set; } = "";
    public string MarketCondition { get; set; } = "Neutral";
    public int Confidence { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class ScannerResult
{
    public string Symbol { get; set; } = "";
    public string Signal { get; set; } = "WAIT";
    public int Confidence { get; set; }
    public decimal Price { get; set; }
    public string MarketCondition { get; set; } = "Neutral";
    public decimal Rsi { get; set; }
    public decimal Ema9 { get; set; }
    public decimal Ema21 { get; set; }
}
