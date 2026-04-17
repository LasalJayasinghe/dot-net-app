public class TradingBotService
{
    private readonly IStrategy _strategy;
    private readonly ILogger<TradingBotService> _logger;
    private readonly List<Candle> _candles = new();
    private Position _position = new();

    private readonly int _maxCandles = 200;
    private decimal _walletBalance = 10000m; // Starting with $10,000
    private readonly decimal _riskPercent = 0.02m;     // 2% risk per trade
    private readonly decimal _riskRewardRatio = 2m;    // 1:2 RR

    public TradingBotService(IStrategy strategy, ILogger<TradingBotService> logger)
    {
        _strategy = strategy;
        _logger = logger;
    }

    public Task OnCandleClosed(Candle candle)
    {
        // 1. STORE candle
        _candles.Add(candle);

        if (_candles.Count > _maxCandles)
            _candles.RemoveAt(0);

        // 2. NOT ENOUGH DATA → skip
        if (_candles.Count < 30)
            return Task.CompletedTask;

        if (_position.IsOpen)
        {
            var currentPrice = candle.Close;

            // STOP LOSS HIT
            if (currentPrice <= _position.StopLoss)
            {
                Console.WriteLine($"STOP LOSS HIT @ {currentPrice}");

                _walletBalance += _position.Quantity * currentPrice;
                _position = new Position();

                return Task.CompletedTask;
            }

            // TAKE PROFIT HIT
            if (currentPrice >= _position.TakeProfit)
            {
                Console.WriteLine($"TAKE PROFIT HIT @ {currentPrice}");

                _walletBalance += _position.Quantity * currentPrice;
                _position = new Position();

                return Task.CompletedTask;
            }
        }

        // 3. ASK STRATEGY
        var signal = _strategy.Evaluate(_candles, _position);

        if (signal == null)
            return Task.CompletedTask;

        Console.WriteLine($"Signal Test: {signal.Type} @ {signal.Price} - {signal.Reason}");

        // 4. EXECUTE TRADE
        if (signal.Type == "BUY" && !_position.IsOpen)
        {
            var entry = signal.Price;

            // 1. Calculate Stop Loss (2% below entry)
            var stopLoss = entry * (1 - _riskPercent);

            // 2. Calculate Take Profit (RR = 1:2 → 4% above)
            var takeProfit = entry * (1 + (_riskPercent * _riskRewardRatio));

            // 3. Position size (still using full balance for now)
            var quantity = Math.Round(_walletBalance / entry, 6);

            _position = new Position
            {
                IsOpen = true,
                EntryPrice = entry,
                Quantity = quantity,
                StopLoss = stopLoss,
                TakeProfit = takeProfit
            };

            _walletBalance -= quantity * entry;

            _logger.LogInformation($"BUY Signal: {signal.Reason} @ {entry}");
            _logger.LogInformation($"Wallet after BUY: {_walletBalance:C}");

            Console.WriteLine($"BUY @ {entry}");
            Console.WriteLine($"SL: {stopLoss} | TP: {takeProfit}");
        }
        else if (signal.Type == "SELL" && _position.IsOpen)
        {
            _logger.LogInformation($"SELL Signal: {signal.Reason} @ {signal.Price}");
            _walletBalance += _position.Quantity * signal.Price;
            _position = new Position { IsOpen = false };
            Console.WriteLine($"Sold at {signal.Price}, new balance: {_walletBalance:C}");
            _logger.LogInformation($"Wallet after SELL: {_walletBalance:C}");
        }

        _logger.LogInformation($"Signal: {signal.Type} @ {signal.Price}");
        return Task.CompletedTask;
    }
}
