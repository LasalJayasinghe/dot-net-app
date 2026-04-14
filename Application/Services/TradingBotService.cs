public class TradingBotService
{
    private readonly IStrategy _strategy;
    private readonly ILogger<TradingBotService> _logger;
    private readonly List<Candle> _candles = new();
    private Position _position = new();

    private readonly int _maxCandles = 200;
    private decimal _walletBalance = 10000m; // Starting with $10,000

    public TradingBotService(IStrategy strategy, ILogger<TradingBotService> logger)
    {
        _strategy = strategy;
        _logger = logger;
    }

    public Task OnCandleClosed(Candle candle)
    {
        // 1. STORE candle
        _candles.Add(candle);
        Console.WriteLine($"Candle count: {_candles.Count}");
        if (_candles.Count > _maxCandles)
            _candles.RemoveAt(0);

        // 2. NOT ENOUGH DATA → skip
        if (_candles.Count < 30)
            return Task.CompletedTask;

        // 3. ASK STRATEGY
        var signal = _strategy.Evaluate(_candles, _position);

        if (signal == null)
            return Task.CompletedTask;

        // 4. EXECUTE TRADE
        if (signal.Type == "BUY" && !_position.IsOpen)
        {
            var quantity = Math.Round(_walletBalance / signal.Price, 6); // Use all balance
            _logger.LogInformation($"BUY Signal: {signal.Reason} @ {signal.Price}");
            _position = new Position
            {
                IsOpen = true,
                EntryPrice = signal.Price,
                Quantity = quantity
            };
            _walletBalance -= quantity * signal.Price;
            _logger.LogInformation($"Wallet after BUY: {_walletBalance:C}");
        }
        else if (signal.Type == "SELL" && _position.IsOpen)
        {
            _logger.LogInformation($"SELL Signal: {signal.Reason} @ {signal.Price}");
            _walletBalance += _position.Quantity * signal.Price;
            _position = new Position { IsOpen = false };
            _logger.LogInformation($"Wallet after SELL: {_walletBalance:C}");
        }

        _logger.LogInformation($"Signal: {signal.Type} @ {signal.Price}");
        return Task.CompletedTask;
    }
}