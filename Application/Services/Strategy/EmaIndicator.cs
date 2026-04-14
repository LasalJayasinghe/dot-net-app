public class EmaIndicator
{
    private readonly int _period;
    private decimal? _ema;

    public EmaIndicator(int period)
    {
        _period = period;
    }

    public decimal Calculate(List<decimal> prices)
    {
        var k = 2m / (_period + 1);

        foreach (var price in prices)
        {
            _ema = _ema == null
                ? price
                : (price * k) + (_ema.Value * (1 - k));
        }

        return _ema ?? 0;
    }
}