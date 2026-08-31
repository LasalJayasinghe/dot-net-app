# .NET MVC Trading & Notification App

> A personal project to monitor stock and cryptocurrency data, send alerts, and track activity with .NET MVC and MySQL.

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Features](#features)
   - [Completed](#completed)
   - [TODO List](#todo-list)
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

- **Dashboard Interface (React)**
  - Modern SPA built with Vite, React Router, and TailwindCSS
  - Watchlist and Portfolio tracking
  - Interactive charts using Recharts and Lightweight Charts
  - Settings and User Profile pages
  - Individual stock detail view with a dividend snapshot panel
  - CSE news view with dividend-payment-date emphasis

- **User Authentication**
  - Google OAuth Integration for the frontend
  - Basic login system
  - Roles: `User` / `Admin`

- **Stock & Crypto Data Integration**
  - Connected to **CSE API** for live stock prices
  - Connected to **Binance WebSocket API** for real-time crypto updates

- **Dividend Ingestion & Storage (CSE)**
  - Background worker fetches approved dividend announcements from CSE
  - Saves dividend records into `Dividends` table
  - Auto-generates linked dividend news entries in `NewsArticles`

- **Market News Pipeline**
  - Multi-provider ingestion and model validation flow
  - Symbol-specific news feed support for stock detail pages
  - Dividend payment date surfaced in API responses for UI prioritization

- **Algorithmic Trading & Alerts**
  - Base logic for BTC trading algorithms
  - Configurable alerts panel in the dashboard

- **Telegram Bot**
  - Base integration ready for alerts
  - Sends messages to selected users

- **Backend & Logging**
  - .NET 9 MVC / Web API architecture
  - Serilog implemented with automatic daily log files
  - Hosted jobs for alerts, exchange-rate sync, news ingestion/validation, and CSE dividends

- **Recent API Additions**
  - `GET /api/dividends/symbol/{symbol}`: dividend records per stock symbol
  - `GET /api/news` and `GET /api/news/symbol/{symbol}` now include dividend payment date metadata when available

---

### TODO List

- **Finalize Alert System**
  - Link frontend alert configurations to backend processing logic.
  - Implement the actual trigger mechanism for sending Telegram notifications based on CSE and Binance data.

- **Trading Algorithm Execution**
  - Complete the BTC trading logic using the Binance API.
  - Build visualization for algorithm backtesting in the frontend.

- **Authentication & Security**
  - Finalize secure token exchange (JWT) between the React frontend and the .NET backend.
  - Implement advanced user roles and permission checks on API endpoints.

- **Historical Data & Analytics**
  - Aggregate historical data for better charting in the dashboard.
  - Set up automated daily MySQL backups.

- **Dividend UX Enhancements**
  - Add upcoming dividend payment countdown and calendar export support.
  - Add richer parsing/normalization for CSE payment date formats.

- **DevOps & Testing**
  - Write Unit & Integration tests for critical API routes.
  - Create Dockerfiles and deployment scripts for the backend and frontend.

---

## Tech Stack

- **Backend:** .NET 9 MVC / Web API
- **Database:** MySQL
- **Logging:** Serilog
- **APIs:** CSE API, Binance WebSocket API
- **Notifications:** Telegram Bot API
- **Frontend:** React, Vite, TailwindCSS, TanStack (Router/Query), Recharts, Google OAuth (in `insight-dashboard`)

<!-- need to look into semantic kernel, (replace ai agent layer with it) -->
