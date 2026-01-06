# .NET MVC Trading & Notification App

> A personal project to monitor stock and cryptocurrency data, send alerts, and track activity with .NET MVC and MySQL.

---

## Table of Contents
1. [Project Overview](#project-overview)  
2. [Features](#features)  
   - [Completed](#completed)  
   - [In Progress](#in-progress)  
   - [Planned](#planned)  
3. [Tech Stack](#tech-stack)  
---

## Project Overview
This application is designed for **tracking financial data**, including stocks and cryptocurrencies, and sending notifications via **Telegram**.  

The goals of this project are:  
- Practice backend development with **.NET MVC**  
- Integrate real-time APIs for finance data  
- Implement logging, authentication, and notifications  
- Build a personal platform for testing trading strategies  

---

## Features

### Completed
- **Logging**
  - Serilog implemented
  - Daily log files created automatically

- **User Authentication**
  - Basic login system
  - Roles: `User` / `Admin`

- **Stock Data Integration**
  - Connected to **CSE API** for live stock prices

- **Cryptocurrency Data**
  - Connected to **Binance WebSocket API**
  - Real-time crypto updates

- **Telegram Bot**
  - Sends messages to selected users
  - Base integration ready for alerts

---

### In Progress
- **Alert System**
  - Notifications based on CSE data
  - Telegram integration for alerts

- **UI Improvements**
  - Updating views for usability and clarity

- **Trading Algorithm**
  - BTC trading logic using Binance API

---

### Planned / Future
- Advanced **user roles and permissions**
- **Historical data analytics** with charts
- Additional notification channels: Email, Push  
- **Dashboard**: portfolio overview, live prices, alert summary  
- **Unit & Integration testing**
- **Dockerization / Deployment scripts**
- Automated **daily MySQL backups**

---

## Tech Stack
- **Backend:** .NET 9 MVC  
- **Database:** MySQL  
- **Logging:** Serilog  
- **APIs:** CSE API, Binance WebSocket API  
- **Notifications:** Telegram Bot API  
- **Frontend:** Razor Views / Bootstrap (Angular planned)

---



<!-- need to  -->
Registration form
Alert managing
Alert related cron