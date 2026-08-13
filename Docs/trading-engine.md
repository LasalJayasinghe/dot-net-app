# Trading Engine Architecture

The trading engine is designed to execute strategies asynchronously based on market data feeds (Binance WebSockets).

## Components

- **IStrategy / EmaRsiStrategy**: Analyzes market data (candles) and returns trading signals (`BUY` / `SELL` / `HOLD`).
- **TradingBotService**: Subscribes to the Binance data feed, passes data to the strategy, and executes trades.
- **PaperTradingState**: Represents the simulated execution environment, starting with $1,000 USD. It manages balances, fees, and position state.

## Data Flow

1. `BinanceService` receives WebSocket updates and publishes `CandleClosed` events.
2. `TradingBotService` handles the event and evaluates `EmaRsiStrategy`.
3. If a signal is generated, `TradingBotService` executes a simulated trade in `PaperTradingState` and logs it to `PaperTrades.log` and `PaperTradingState.json`.
