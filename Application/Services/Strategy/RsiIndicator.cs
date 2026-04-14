public class RsiIndicator
{
    private readonly int _period;

    public RsiIndicator(int period)
    {
        _period = period;
    }

    public decimal Calculate(List<decimal> prices)
    {
        if (prices.Count < _period + 1)
            return 50;

        decimal gain = 0;
        decimal loss = 0;

        for (int i = prices.Count - _period; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];

            if (change > 0)
                gain += change;
            else
                loss += Math.Abs(change);
        }

        if (loss == 0) return 100;

        var rs = gain / loss;

        return 100 - (100 / (1 + rs));
    }
}