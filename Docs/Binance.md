# Binance Documentation

This document provides a detailed overview of the Binance integration, including WebSocket handling, candle data, trading strategy, and related services.

## Overview
The Binance module is responsible for:
- Connecting to Binance WebSocket streams for real-time crypto data
- Processing and storing candle (kline) data
- Providing event-driven updates for closed candles
- Supporting crypto price monitoring, alerting, and trading strategies

## Data Flow
1. **BinanceService** connects to the Binance WebSocket and listens for kline (candle) events.
2. When a candle closes, it is processed and can trigger events or notifications.
3. Candle data can be used for analytics, alerts, or dashboard updates.

## Key Classes & Files
- **Application/Services/BinanceService.cs**: Handles WebSocket connection, message parsing, and candle processing.
- **Domain/Entities/Binance/Candle.cs**: Represents a single candle (OHLCV) data point.
- **Controllers/BinanceController.cs**: (If implemented) Exposes endpoints for Binance-related data.

## Main Features
- **WebSocket Integration**: Real-time connection to Binance for crypto price and candle updates.
- **Candle Processing**: Extracts OHLCV data from WebSocket messages.
- **Event Handling**: (If implemented) Triggers events when a candle closes for further processing.
- **Strategy Execution**: Implements trading or alerting strategies based on candle data.

## Example: Candle Entity
```csharp
public class Candle {
    public string Symbol { get; set; }
    public DateTime OpenTime { get; set; }
    public DateTime CloseTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}
```

## WebSocket Message Handling
- The service listens for messages, parses JSON, and checks if a candle is closed (`x: true`).
- Closed candles are processed and can be used for analytics or notifications.

## Strategy: How It Works
The Binance integration can be used to implement trading or alerting strategies. Here is a typical flow:

1. **Receive Candle Data**: On each closed candle, the service receives OHLCV data.
2. **Analyze Candle**: The strategy analyzes the candle's open, high, low, close, and volume values.
3. **Signal Generation**: Based on predefined rules (e.g., moving average crossover, price breakout, volume spike), the strategy determines if a buy/sell/hold signal should be generated.
4. **Action Execution**: If a signal is generated, the system can:
   - Send a notification (e.g., via Telegram)
   - Place a simulated or real trade (if trading is enabled)
   - Log the event for analytics
5. **Repeat**: The process repeats for each new closed candle.

### Example Strategy: Simple Moving Average Crossover
- Calculate the short-term and long-term moving averages from recent candle closes.
- If the short-term MA crosses above the long-term MA, generate a "buy" signal.
- If the short-term MA crosses below the long-term MA, generate a "sell" signal.

### Example Pseudocode
```csharp
void OnCandleClosed(Candle candle) {
    UpdateMovingAverages(candle.Close);
    if (ShortMA > LongMA && PreviousShortMA <= PreviousLongMA) {
        // Buy signal
        Notify("Buy signal generated");
    } else if (ShortMA < LongMA && PreviousShortMA >= PreviousLongMA) {
        // Sell signal
        Notify("Sell signal generated");
    }
}
```

## Usage Example
- The BinanceService runs as a background service and maintains a live connection to Binance.
- Candle data can be accessed or subscribed to for real-time trading strategies or dashboards.
- Strategies can be implemented by subscribing to candle close events and applying custom logic.

## Related Features
- Real-time crypto price monitoring
- Integration with alerting or trading bots
- Dashboard display of crypto candles

---
