# Stock Documentation

Stock (CSE) features, services, data models and APIs.

## Overview
The stock module:
- fetches prices, indices and market status from the CSE API (cse.lk) and stores the latest snapshot
- serves real price history from CSE for charts and technical summaries
- fetches and stores CSE dividend announcements
- provides stock APIs for the web and mobile apps and the AI assistant
- drives price alerts (Telegram and push)

## Data flow
1. **AlertJob** polls CSE through **StockService**:
   - trade summary every 2 min while the market is open (60 min when closed)
   - indices and market status
   - it upserts the `Stocks`, `CseMarketIndices` and `CseMarketStatus` rows
2. **StockApiController** serves stock data from the database. Price history is fetched live from CSE and cached.
3. **CseDividendJob** fetches CSE dividend announcements hourly and upserts `Dividends`, plus a news article per announcement.
4. **NewsApiController** adds `dividendPaymentDate` to dividend news articles.
5. **AlertService** compares stored prices with user alerts and notifies through Telegram and Expo push.

## Key files
- `Application/Services/StockService.cs`:
  - CSE client (15s timeout, one retry)
  - trade summary sync and price history
  - also holds `CseSymbols`, the symbol helper (`COMB` → `COMB.N0000`)
- `Controllers/Api/StockApiController.cs`: stock endpoints (below)
- `Controllers/Api/WatchlistApiController.cs`: user watchlist, priced from the `Stocks` table
- `Jobs/AlertJob.cs`, `Jobs/CseDividendJob.cs`: background polling
- `Controllers/Api/DividendsApiController.cs`, `Controllers/Api/NewsApiController.cs`
- `Application/Dtos/StockDataDtos.cs`: CSE response DTOs
- `Domain/Entities/CSE/Stock.cs`, `MarketStatus.cs`, `MarketIndices.cs`: entities
- `Infrastructure/Repositories/StockRepository.cs`: persistence and trading-day detection
- `Application/Services/Ai/Tools/GetStockDataTool.cs`, `GetMarketStatusTool.cs`: AI assistant tools

## API (`/api/stocks`, JWT required)

| Endpoint | Description |
|---|---|
| `GET details/{symbol}` | Live CSE quote. Accepts `COMB` or `COMB.N0000` |
| `GET {symbol}/history?range=1D\|1W\|1M\|3M\|1Y` | Real CSE price history, oldest first. `1D` = today's trades; other ranges are daily bars from about one year of history |
| `GET summary` | Top 50 stocks by % change, from the database |
| `GET intraday` | Most volatile stocks today |
| `GET movers` | Top 10 gainers and top 10 losers (live CSE `topGainers`/`topLooses`) |
| `GET most-active?limit=10` | `{ byVolume, byTurnover }`: today's top stocks by share volume and by turnover, from the in-memory tradeSummary snapshot |
| `GET indices` | ASPI and S&P SL20 (stored) |
| `GET market-status` | Open/closed and trading day |
| `GET names` | Symbol and name list (cached 1h) |

### Example
```http
GET /api/stocks/details/ABAN
```
```json
{ "symbol": "ABAN.N0000", "price": 1018.5, "lastTradedPrice": 1018.5 }
```

```http
GET /api/stocks/JKH/history?range=1M
```
```json
[{ "time": 1760380200, "open": 19.5, "high": 19.9, "low": 19.4, "close": 19.7, "volume": 8313818 }]
```
`time` is Unix seconds (UTC). CSE daily data has no open price, so `open` is the previous close.

## CSE endpoints used
All are POST requests to `https://www.cse.lk/api/...`; see `mark document/CSE Research Automation/Task.md` for the probe notes.

| Endpoint | Used for |
|---|---|
| `tradeSummary` | All ~294 stocks: OHLC, volume, turnover, market cap, and the internal `id` |
| `companyInfoSummery` (`symbol`) | Single-stock quote |
| `companyChartDataByStock` (`stockId`, `period`) | Price history: `1` = intraday, `5` = about 1 year daily |
| `marketStatus`, `aspiData`, `snpData`, `topGainers`, `topLooses` | Market overview |
| `approvedAnnouncement`, `getAnnouncementById` | Dividend announcements (`CseDividendJob`) |

## Trading day
`IsTradingDay` is true on a weekday only if CSE actually opens: before 09:45 Colombo time it is assumed true, after that only if the market was reported open that day. This treats holidays correctly.

## Portfolio trading desk (`/api/portfolios`)

**`GET {id}` holdings** include:
- `breakEvenPrice` (BES)
- `previousClose`, `dayChange`, `dayChangePercent`, `dayProfitLoss`
- `weightPercent`
- `shareVolume`, `tradeVolume`, `turnover` (today, market-wide for that security)

**The portfolio** includes `todayProfitLoss` and `todayProfitLossPercent`.

**`GET {id}/insights`** (CSE portfolios only):
- signals per holding, from ~1 year of CSE daily prices: trend vs SMA50/200, RSI14, near the 52-week high/low, volume spike
- `upcomingDividends`: dividends with a future record date for held symbols, with `expectedPayout = qty × DPS`

**Fees** (`Domain/Common/CseFees.cs`, matching ATrad). CSE charges are 1.12% on each side, and the average buy price is the raw trade price:
- **Cost** = qty × avg × 1.0112
- **P/L** = value × 0.9888 − cost
- **Cost/share** = avg × 1.0112: what you paid per share, including the buy fee. Selling at this price still loses the 1.12% sell fee.
- **BES** = (buy value + buy fees) / (qty × (1 − 1.12%)) = avg × 1.0112 / 0.9888

P/L is exactly 0 at the BES. Example: avg 100 → Cost/share 101.12, BES 102.27.

**Volume/turnover storage:** these are intraday values, so they are kept in memory only (`StockService.GetCachedActivity`, refreshed on each tradeSummary poll). There are no database columns and no migration.

## Price alerts on indices, crypto and FX (`/api/alerts`)
Besides CSE stocks, alerts accept these symbols (no dot, so they can't clash with CSE codes). `MarketPriceResolver` prices them:

| Symbol | What | Price source | Checked |
|---|---|---|---|
| `ASPI` | ASPI index | `CseMarketIndices` (5 min while open) | every 2 min, 24/7 |
| `SNPSL20` | S&P SL20 index | `CseMarketIndices` | every 2 min, 24/7 |
| `BTCUSDT`, `SOLUSDT` | Binance price in USDT | live WebSocket ticker | every 2 min, 24/7 |
| `USDTLKR` | USDT→LKR rate | Binance P2P (`ExchangeRateJob`, hourly) | every 2 min, 24/7 |

- **CSE stock alerts** keep the CSE schedule: every 2 min while the market is open, hourly when closed.
- **POST validation:** the symbol must be supported, and CSE codes are normalised (`COMB` → `COMB.N0000`). The target price must be > 0.
- **Re-firing:** alerts stay active and re-notify while the condition holds (intended).
- **Push type:** market alerts send `type = "market-alert"`, so the mobile app doesn't open a stock page for them.
