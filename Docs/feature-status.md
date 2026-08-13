# Feature Status Report

| Feature | Status | Notes |
|---------|--------|-------|
| Authentication | Existing | Google OAuth and JWT Tokens implemented in AuthApiController |
| Authorization | Existing | Role-based (User / Admin) |
| Portfolio | Existing | Multi-portfolio support for Stocks/Crypto, PDF Sync |
| Watchlist | Existing | Watchlist items per user |
| CSE market data | Existing | StockService integration |
| Binance WebSocket | Existing | BinanceService integration |
| Alerts | Existing | Alert configuration and background processing (`AlertJob.cs`) |
| Telegram | Existing | TelegramService sends alerts |
| Trading strategies | Existing | EMA/RSI logic implemented in `TradingBotService` / `EmaRsiStrategy` |
| Historical data | Missing / Partial | No extensive historical charting backend endpoints identified |
| Backtesting | Missing / Partial | Paper trading / Simulation logic is incomplete, currently only partially implemented in `TradingBotService` |
| Dashboard | Existing | React SPA frontend provides modern dashboard |
| Logging | Existing | Serilog implemented |
| Background services | Existing | `AlertJob.cs` evaluates alerts in the background |
