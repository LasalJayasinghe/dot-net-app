public interface IStrategy
{
    Signal? Evaluate(List<Candle> candles, Position position);
}