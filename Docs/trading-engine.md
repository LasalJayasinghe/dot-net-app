# Trading Engine Architecture

`TradingEngine` runs every registered `ITradingStrategy` as an independent **paper-trading** bot on public
Binance market data. No API keys are used and no real orders can be placed.

## Components (`Application/Trading/`)

| Folder | What |
|---|---|
| `Market/` | Feed definitions (`CandleFeed`, depth, aggTrade, funding) |
| `Indicators/` | Streaming O(1) indicators: EMA, SMA, RSI (Wilder), MACD, ATR, Bollinger, Supertrend, rolling VWAP, rolling max/min (monotonic deque), rate of change |
| `Engine/` | `TradingEngine` (hosted service), `BinanceMarketClient` (WebSocket + REST), `BotRunner` (per-bot state/persistence), `PaperAccount` (fills + fees), `StrategyContext` (the order API strategies use) |
| `Strategies/` | The 5 active strategies below, plus the retired ones (kept in code for backtests, not registered) |

## Data flow

1. On startup the engine collects every strategy's feeds, opens **one** combined Binance WebSocket
   (`/stream?streams=…`) for all kline / depth20 / aggTrade streams, and polls futures funding every 5 min.
2. Each feed is warmed up from REST history (`/api/v3/klines`), replayed in time order with trading disabled,
   so bots are ready immediately after a restart.
3. Live events go through a single-reader channel and are routed only to subscribed bots.
   Missed candles after a reconnect are backfilled from REST as warm-up (no trades at stale prices).
4. A bot's state is saved to `PaperTrading/{id}.json` only when it changes (trade, signal, toggle).
   The original `PaperTradingState.json` was imported into `ema-rsi-btc` (now retired, so the import no longer runs).
   JSON files of retired bots stay on disk untouched and are simply not loaded.
5. `GET /api/algorithms` reads bot state in memory. No database is used.

## Fees

Every executed side pays `PaperTrading__FeeRate` (default 0.001 = 0.10%, Binance spot standard).
- **BUY:** spends at most the available cash including the fee.
- **SELL:** proceeds are net of the fee.
- **Realized P/L:** sell proceeds minus cost basis. The cost basis includes the BUY fee, so P/L is net of both fees.
- **Average cost:** used by default. The grid bot sells specific lots instead.
- **Equity / unrealized P/L:** open positions are valued as if sold now, after the exit fee.

## Benchmark: holding BTC

Every bot is compared with simply buying BTC with its starting balance on the day it started and holding it.
- **Start price:** `BotState.BenchmarkBtcPrice` is the BTC/USDT price at `CreatedAt`. The engine records it once after warm-up. For bots that already existed, it takes the open of the Binance 1-minute candle at their start time. A failed lookup retries on the next restart.
- **API fields** (list and detail): `startedAt`, `btcStartPrice`, `btcPrice`, `btcHoldValue`, `btcHoldPnlPct`, `vsBtc` (equity − BTC-hold value, USDT) and `vsBtcPct` (the same, in % points of the starting balance).
- **Fees:** the BTC-hold value pays the buy fee and deducts the exit fee, the same way equity is valued, so the two numbers are directly comparable.

## Strategies

The current set comes from a 2-year and a 6-year backtest on real Binance data (fees + slippage included). The
research, tables and backtest code are in `Desktop/mark document/Crypto Algo Research/` (`Findings.md`).

### Active
| Id | Category | Feed | Rules |
|---|---|---|---|
| `trend-rotation-6` | Multi-asset | BTC/ETH/SOL/BNB/XRP/DOGE 4h | Once a day at 00:00 UTC: if BTC is above its 100-day average, hold the **top 2** coins by risk-adjusted 7/14/30-day momentum (only coins above their own 50-day average), weighted by inverse volatility; otherwise cash. Keeps a coin while it ranks ≤ 3 or for 48h, re-weights only if > 25% off target. **3×ATR(4h) trailing stop** per coin |
| `trend-rotation-6-calm` | Multi-asset | same | Same rules, **top 3** coins, **no trailing stop** |
| `ema-cross-btc-4h` | Trend | BTC 4h | Long while EMA50 > EMA200, cash otherwise |
| `dca-btc` | Systematic | BTC 1h | Benchmark. Weekly buy of 1/104 of the starting balance (~2 years), double when ≥ 5% below the 200h SMA. Never sells |
| `grid-btc` | Systematic | BTC 1m | 10 levels 1% apart, $100 each. Each lot sells one level up (~0.8% net after fees). Kept as a sideways-market sleeve |

Backtest highlights (0.10% fee + 0.05% slippage per side):

| Bot | 18 Nov 2024 → 18 Nov 2026 | 2021 → Nov 2026 (coin list as it was each year) |
|---|---|---|
| `trend-rotation-6` | +44% (BTC −28%), worst drop 33% | +527% (BTC +121%), worst drop 48% |
| `trend-rotation-6-calm` | +38%, worst drop 22% | +1,632%, worst drop 45% |
| `ema-cross-btc-4h` | +13%, worst drop 27% | +375% (today's coins), worst drop 60% |

Multi-coin bots (`trend-rotation-*`) can hold several coins at once; every holding shows under `positions` in
`GET /api/algorithms/{id}`. Their minimum-hold and cooldown timers are saved in the bot's JSON (`StrategyState`).

### Retired (not registered; classes kept for backtests)
| Id | Why it was retired (2-year backtest, BTC buy & hold −28%) |
|---|---|
| `ema-rsi-btc` | Its two conditions (EMA9 > EMA21 and RSI < 30) were never true together on 210,240 5m bars, so it can't trade |
| `macd-btc-1h` | −88%; flat before fees, 674 trades |
| `donchian-btc-1h` | −61%; 20-hour channel is noise |
| `supertrend-btc-1h` | −67%; negative edge even before fees |
| `ema-cross-btc-1h` | −73%; whipsawed. Replaced by `ema-cross-btc-4h` |
| `bb-reversion-btc-15m` | −96%; average win smaller than average loss, 1,064 trades |
| `rsi-reversion-btc-15m` | −94% |
| `vwap-reversion-btc-1h` | −68%; keeps buying in downtrends |
| `bb-squeeze-btc-1h` | −24%; few signals, no edge |
| `momentum-rotation-1h` | −90%: +33% before fees, but 798 trades paid $1,230 in fees. Replaced by `trend-rotation-6` |
| `eth-btc-pairs-1h` | −45%; ETH/BTC trends instead of reverting |
| `orderbook-imbalance-btc` | Live paper: −$122, 96% of it fees; needs a 54–62% win rate just to break even |
| `whale-follow-btc` | Live paper: −$73, same fee bleed |
| `funding-contrarian-btc` | BTC funding hit −0.01% only 9 times in 2 years; looser versions lost money |

To re-enable one, add its registration back in `Program.cs`.

To add a strategy, subclass `TradingStrategyBase`, declare its `Feeds`, and register it in `Program.cs` as
`ITradingStrategy`. The engine subscribes to its streams and the Algorithms view lists it automatically.
