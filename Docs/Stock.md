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
| `GET movers` | Top gainers and losers (live CSE) |
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
