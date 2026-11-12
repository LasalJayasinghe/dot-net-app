# Trading Engine Architecture

`TradingEngine` runs every registered `ITradingStrategy` as an independent **paper-trading** bot on public
Binance market data. No API keys are used and no real orders can be placed.

## Components (`Application/Trading/`)

| Folder | What |
|---|---|
| `Market/` | Feed definitions (`CandleFeed`, depth, aggTrade, funding) |
| `Indicators/` | Streaming O(1) indicators: EMA, SMA, RSI (Wilder), MACD, ATR, Bollinger, Supertrend, rolling VWAP, rolling max/min (monotonic deque), rate of change |
| `Engine/` | `TradingEngine` (hosted service), `BinanceMarketClient` (WebSocket + REST), `BotRunner` (per-bot state/persistence), `PaperAccount` (fills + fees), `StrategyContext` (the order API strategies use) |
| `Strategies/` | The 16 strategies below |

## Data flow

1. On startup the engine collects every strategy's feeds, opens **one** combined Binance WebSocket
   (`/stream?streams=…`) for all kline / depth20 / aggTrade streams, and polls futures funding every 5 min.
2. Each feed is warmed up from REST history (`/api/v3/klines`), replayed in time order with trading disabled,
   so bots are ready immediately after a restart.
3. Live events go through a single-reader channel and are routed only to subscribed bots.
   Missed candles after a reconnect are backfilled from REST as warm-up (no trades at stale prices).
4. A bot's state is saved to `PaperTrading/{id}.json` only when it changes (trade, signal, toggle).
   The original `PaperTradingState.json` is imported into `ema-rsi-btc` the first time.
5. `GET /api/algorithms` reads bot state in memory. No database is used.

## Fees

Every executed side pays `PaperTrading__FeeRate` (default 0.001 = 0.10%, Binance spot standard).
- **BUY:** spends at most the available cash including the fee.
- **SELL:** proceeds are net of the fee.
- **Realized P/L:** sell proceeds minus cost basis. The cost basis includes the BUY fee, so P/L is net of both fees.
- **Average cost:** used by default. The grid bot sells specific lots instead.
- **Equity / unrealized P/L:** open positions are valued as if sold now, after the exit fee.

## Strategies

| Id | Category | Feed | Rules |
|---|---|---|---|
| `ema-rsi-btc` | Trend | BTC 5m | Original bot. Buy: EMA9 > EMA21 and RSI14 < 30. Sell: EMA9 < EMA21 |
| `macd-btc-1h` | Trend | BTC 1h | MACD(12,26,9) signal-line crossovers |
| `donchian-btc-1h` | Trend | BTC 1h | Buy above the prior 20h high. Exit below the prior 10h low |
| `supertrend-btc-1h` | Trend | BTC 1h | Supertrend(10, 3×ATR) flips |
| `ema-cross-btc-1h` | Trend | BTC 1h | EMA 9/21 crossovers |
| `bb-reversion-btc-15m` | Mean reversion | BTC 15m | Buy below the lower band if the middle band is ≥ 0.6% away. Exit at the middle band. 2×ATR stop |
| `rsi-reversion-btc-15m` | Mean reversion | BTC 15m | Buy RSI < 30. Sell RSI > 55. 2×ATR stop |
| `vwap-reversion-btc-1h` | Mean reversion | BTC 1h | Buy ≥ 1.5% below the 24h VWAP. Exit at VWAP. 2×ATR stop |
| `bb-squeeze-btc-1h` | Volatility breakout | BTC 1h | Bandwidth at a 120h low, then a close above the upper band. 2×ATR stop, 4×ATR target |
| `momentum-rotation-1h` | Multi-asset | 7 coins 1h | Every 4h, hold the best 24h performer (cash if all are negative). Switch only if 1%+ better |
| `eth-btc-pairs-1h` | Multi-asset | ETH + BTC 1h | ETH/BTC ratio z-score ±2 over 100h. Buy the cheap leg, exit at the mean. 5% stop |
| `dca-btc` | Systematic | BTC 1h | $25/day, or $50 when ≥ 5% below the 200h SMA. Never sells |
| `grid-btc` | Systematic | BTC 1m | 10 levels 1% apart, $100 each. Each lot sells one level up (~0.8% net after fees) |
| `orderbook-imbalance-btc` | Order flow | BTC depth20 + 1m | Smoothed bid/ask imbalance > 25% and price above EMA20. +0.8% / −0.5%, 60m time stop |
| `whale-follow-btc` | Order flow | BTC aggTrade + 1m | Net ≥ $1.5M of ≥ $100k taker buys in 5m, buying 2:1 over selling. +1% / −0.6%, 90m time stop |
| `funding-contrarian-btc` | Order flow | BTC funding + 1h | Buy spot when funding ≤ −0.01%. Exit at ≥ +0.03%. 2×ATR stop, 3×ATR target |

To add a strategy, subclass `TradingStrategyBase`, declare its `Feeds`, and register it in `Program.cs` as
`ITradingStrategy`. The engine subscribes to its streams and the Algorithms view lists it automatically.
