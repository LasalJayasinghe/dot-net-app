public class EmaRsiStrategy : IStrategy
{
    private readonly EmaIndicator _emaShort = new(9);
    private readonly EmaIndicator _emaLong = new(21);
    private readonly RsiIndicator _rsi = new(14);

    public Signal? Evaluate(List<Candle> candles, Position position)
    {
        if (candles.Count < 30)
            return null;

        var closes = candles.Select(x => x.Close).ToList();

        var emaS = _emaShort.Calculate(closes);
        var emaL = _emaLong.Calculate(closes);
        var rsi = _rsi.Calculate(closes);

        var lastPrice = closes.Last();

        // 🟢 BUY LOGIC
        if (!position.IsOpen &&
            emaS > emaL &&
            rsi < 30)
        {
            return new Signal
            {
                Type = "BUY",
                Price = lastPrice,
                Reason = "EMA crossover + RSI oversold"
            };
        }

        // 🔴 SELL LOGIC
        if (position.IsOpen &&
            emaS < emaL)
        {
            return new Signal
            {
                Type = "SELL",
                Price = lastPrice,
                Reason = "EMA bearish crossover"
            };
        }

        return null;
    }
}