# Current Architecture

## Overview
The application consists of two main parts:
- **Frontend**: A React SPA built with Vite, TailwindCSS, and TanStack (Router/Query), named `insight-dashboard`.
- **Backend**: A .NET 9 Web API/MVC application named `dot-net-app`, using Entity Framework Core and MySQL.

## Frontend (`insight-dashboard`)
- **Framework**: React 19, Vite
- **Routing**: TanStack Router
- **State/Data**: TanStack Query (React Query)
- **UI Components**: Radix UI, TailwindCSS, Recharts, Lightweight Charts
- **Features**: Dashboard, Portfolios, Watchlist, Alerts, Crypto and Stock Market Data, Algorithms, Authentication (Google OAuth).

## Backend (`dot-net-app`)
- **Framework**: .NET 9 Web API
- **Database**: MySQL via EF Core (`AppDbContext`)
- **Logging**: Serilog

### Controllers/Endpoints
Located in `Controllers/Api`:
- `AlertApiController`, `AlgorithmsApiController`, `AuthApiController`, `CryptoApiController`, `DividendsApiController`, `NewsApiController`, `PortfolioApiController`, `ProfileApiController`, `SettingsApiController`, `StockApiController`, `WatchlistApiController`.
Other:
- `BinanceController`, `TelegramController`.

### Services
Located in `Application/Services`:
- `AiMarketSummaryService`
- `AlertService`
- `BinanceService`
- `CryptoMarketService`
- `CseDividendFetcherService`
- `PdfSyncService`
- `PortfolioService`
- `NewsIngestionService`
- `NewsValidationService`
- `StockService`
- `TelegramService`
- `TokenService`
- `TradingBotService`
- `Strategy/EmaRsiStrategy`

### Background Workers
Located in `Jobs`:
- `AlertJob`
- `CseDividendJob`
- `ExchangeRateJob`
- `NewsIngestionJob`
- `NewsValidationJob`

### Database Entities
Located in `Domain/Entities`:
- `Alert`, `ApplicationUser`, `CurrencyExchangeRate`, `Dividend`, `NewsArticle`, `OtpRecord`, `Portfolio`, `PortfolioHolding`, `Profile`, `WatchlistItem`

### Repositories
Located in `Infrastructure/Repositories`:
- `AlertRepository`, `ProfileRepository`, `StockRepository`

### External Integrations
- **Binance API**: WebSockets via `BinanceService`
- **CSE API**: Stock data via `StockService`; dividend announcements via `CseDividendJob` / `CseDividendFetcherService`
- **Telegram API**: Notifications via `TelegramService`
- **Google OAuth**: Auth handled by `AuthApiController` and `TokenService`
- **Ollama API**: Local model validation for news relevance/sentiment in `NewsValidationService`
