# General Application Documentation

This document provides a detailed overview of the application's architecture, services, controllers, and infrastructure.

## Overview
The application is a .NET MVC-based trading and notification platform for stocks and cryptocurrencies. It integrates with external APIs (CSE, Binance), supports user authentication, and provides real-time notifications via Telegram.

## Architecture
- **Backend:** .NET 9 MVC
- **Database:** MySQL
- **Logging:** Serilog
- **Notifications:** Telegram Bot API
- **Frontend:** Razor Views / Bootstrap

## Main Components
- **Program.cs**: Application entry point, configures services, middleware, and dependency injection.
- **Application/Services/**: Contains business logic for stocks, Binance, alerts, and Telegram.
- **Controllers/**: MVC controllers for handling HTTP requests and API endpoints.
- **Infrastructure/**: Data access, repositories, and database context.
- **Views/**: Razor views for UI rendering.
- **wwwroot/**: Static assets (CSS, JS, images, libraries).

## Key Services
- **StockService**: Handles stock data fetching and updates.
- **BinanceService**: Handles real-time crypto data via WebSocket.
- **AlertService**: Manages user alerts and notifications.
- **TelegramService**: Integrates with Telegram Bot API for notifications.

## Authentication & Authorization
- Uses ASP.NET Identity for user management.
- Policies for dashboard and alert access.
- Cookie-based authentication with custom login and access denied paths.

## Example: Service Registration
```csharp
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<StockService>();
builder.Services.AddSingleton<BinanceService>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<AppDbContext>();
```

## Usage
- Start the application with `dotnet run`.
- Access the dashboard for stock and crypto monitoring.
- Register/login to manage alerts and receive notifications.

## Related Features
- Logging with Serilog
- Database migrations with Entity Framework
- Modular service and controller structure

---
