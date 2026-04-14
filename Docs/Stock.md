# Stock Documentation

This document provides a comprehensive overview of all stock-related features, services, data models, and APIs in the application.

## Overview
The stock module is responsible for:
- Fetching and updating stock data from the CSE API
- Managing stock entities in the database
- Providing APIs for stock data retrieval
- Supporting alerting and notification features based on stock prices

## Data Flow
1. **StockService** fetches data from the CSE API and updates the database.
2. **StockController** exposes endpoints for retrieving stock data.
3. **AlertService** uses stock data to trigger alerts.

## Key Classes & Files
- **Application/Services/StockService.cs**: Core logic for fetching, updating, and caching stock data.
- **Controllers/StockController.cs**: API endpoints for stock data.
- **Application/Dtos/StockDataDtos.cs**: Data transfer objects for stock API responses.
- **Models/Stocks.cs**: Entity model for stocks in the database.

## Main Features
- **Get All Stock Names**: `GetAllStockNamesAsync()` returns a list of all stocks for dropdowns and selection.
- **Get Stock Data**: `GetStockDataAsync(symbol)` fetches and updates stock data for a given symbol.
- **Trading Summary**: `GetTradingSummaryAsync()` fetches and updates trading summary data for all stocks.
- **Alert Integration**: Alerts can be set based on stock price changes.

## Example Usage
- The `StockController` exposes an endpoint `/api/stock/{symbol}` to get stock data.
- The `AlertController` uses `StockService` to populate stock dropdowns and validate alerts.
- The `TelegramController` can fetch stock data and send it to users via Telegram.

## Data Models
- **Stocks**: Represents a stock entity with properties like Symbol, Name, Price, High, Low, PreviousClose, etc.
- **StockDataResponseDto**: DTO for stock data API responses.
- **TradeSummaryItemDto**: DTO for trading summary items.

## Example: Stock Entity
```csharp
public class Stocks {
    public int Id { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public decimal Change { get; set; }
    public decimal PercentageChange { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal ClosingPrice { get; set; }
}
```

## API Example
```http
GET /api/stock/ABAN.N0000
```
Response:
```json
{
  "Symbol": "ABAN.N0000",
  "Price": 123.45,
  "LastTradedPrice": 123.00
}
```

## Related Features
- Alerts based on stock price
- Stock search and dashboard
- Integration with Telegram notifications

---
