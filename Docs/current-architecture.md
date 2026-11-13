# Current Architecture

## Overview
The application has three parts:
- **Web frontend:** `insight-dashboard`, a React SPA built with Vite, TailwindCSS and TanStack Router. It is deployed to Cloudflare.
- **Mobile app:** `alertme-mobile`, an Expo / React Native app that uses the same API.
- **Backend:** `dot-net-app`, a .NET 9 Web API/MVC application using Entity Framework Core with MySQL.

## Web frontend (`insight-dashboard`)
- **Framework:** React 19, Vite
- **Routing:** TanStack Router (file-based)
- **Data:** axios `apiClient` plus plain hooks (`useState`/`useEffect`). TanStack Query is installed but not used yet.
- **UI:** Radix UI, TailwindCSS, Recharts, Lightweight Charts
- **Features:**
  - Dashboard, portfolios, watchlist, alerts
  - CSE stocks: real price history chart and technical summary
  - Crypto terminal
  - Algorithms: the paper-trading bots
  - AI assistant, with streaming replies and tool activity
  - Google OAuth sign-in

## Backend (`dot-net-app`)
- **Framework:** .NET 9 Web API
- **Database:** MySQL via EF Core (`AppDbContext`)
- **Logging:** Serilog

### Controllers / endpoints
Located in `Controllers/Api`:
- `AiChatApiController`, `AlertApiController`, `AlgorithmsApiController`, `AuthApiController`, `CryptoApiController`, `DevicesApiController`, `DividendsApiController`, `NewsApiController`, `PortfolioApiController`, `ProfileApiController`, `SettingsApiController`, `StockApiController`, `WatchlistApiController`

Other:
- `BinanceController` (empty stub), `TelegramController` (bot webhook)

### Services (`Application/Services`)
- **Market data:** `StockService` (CSE), `BinanceService` (Binance WebSockets), `BinanceP2pService`, `CryptoMarketService`, `AiMarketSummaryService`
- **Portfolios:** `PortfolioService`, `PortfolioSnapshotService`, `PortfolioFileSyncService` (broker PDF/Excel statement import via PdfPig / ExcelDataReader)
- **News:** `NewsIngestionService`, `NewsValidationService` (Ollama)
- **AI chat:** `AiAgentService` (Ollama with tool calling). Tools live in `Ai/Tools`:
  - stock price, market status and indices, top gainers/losers, market news
  - Binance ticker
  - portfolio holdings, net worth, profile
- **Notifications and auth:** `AlertService`, `TelegramService`, `PushNotificationService` (Expo), `BrevoEmailService`, `TokenService`

### Paper-trading engine (`Application/Trading`)
- **Engine:** `TradingEngine` (hosted service) runs 16 strategies as independent paper bots on public Binance data. See `Docs/trading-engine.md`.

### Background workers (`Jobs`)
- `AlertJob`, `CseDividendJob`, `ExchangeRateJob`, `NewsIngestionJob`, `NewsValidationJob`, `PortfolioSnapshotJob`
- The trading engine (`TradingEngine`) and `BinanceService` are also hosted services.

### Database entities (`Domain/Entities`)
- **Users and auth:** `ApplicationUser`, `Profile`, `OtpRecord`, `DeviceToken`
- **Portfolios and alerts:** `Alert`, `Portfolio`, `PortfolioHolding`, `PortfolioSnapshot`, `WatchlistItem`
- **Market and news:** `CurrencyExchangeRate`, `Dividend`, `NewsArticle`
- **CSE:** `CSE/Stock`, `CSE/MarketStatus`, `CSE/MarketIndices`

### Repositories (`Infrastructure/Repositories`)
- `AlertRepository`, `ProfileRepository`, `StockRepository`

### External integrations
- **CSE (cse.lk):** prices, indices, market status and price history via `StockService`; dividend announcements via `CseDividendJob`
- **Binance:** WebSockets via `BinanceService` and `TradingEngine`, plus REST klines and trades
- **Telegram:** notifications and bot via `TelegramService` / `TelegramController`
- **Expo push:** `PushNotificationService`
- **Brevo:** email (OTP)
- **Google OAuth:** `AuthApiController` and `TokenService`
- **Ollama (local LLM):** news validation in `NewsValidationService`; AI chat in `AiAgentService`
