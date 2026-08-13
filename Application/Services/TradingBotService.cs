using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using dotnetApp.Application.Models;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services;

public class TradingBotService
{
    private readonly IStrategy _strategy;
    private readonly ILogger<TradingBotService> _logger;
    private readonly List<Candle> _candles = new();
    
    private readonly int _maxCandles = 200;
    private readonly decimal _tradingFeePercent = 0.001m; // 0.10% per executed side
    
    private PaperTradingState _state = new();
    private readonly string _stateFilePath = "PaperTradingState.json";
    private readonly string _logFilePath = "PaperTrades.log";

    public TradingBotService(IStrategy strategy, ILogger<TradingBotService> logger)
    {
        _strategy = strategy;
        _logger = logger;
        LoadState();
    }

    private void LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                _state = JsonSerializer.Deserialize<PaperTradingState>(json) ?? new PaperTradingState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load paper trading state. Starting fresh.");
                _state = new PaperTradingState();
            }
        }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save paper trading state.");
        }
    }

    private void LogTrade(PaperTrade trade)
    {
        var logMessage = $"PAPER {trade.Side} {trade.Symbol}\n" +
                         $"Price: {trade.ExecutionPrice:C}\n" +
                         $"Quantity: {trade.Quantity}\n" +
                         $"Gross: {trade.GrossValue:C}\n" +
                         $"Fee: {trade.Fee:C}\n" +
                         $"Net: {trade.NetValue:C}\n" +
                         $"USD Balance: {_state.UsdBalance:C}\n" +
                         $"BTC Balance: {_state.BtcBalance}\n" +
                         $"Reason: {trade.Reason}\n" +
                         $"Timestamp: {trade.Timestamp}\n" +
                         $"----------------------------------------\n";
                         
        Console.WriteLine(logMessage);
        
        try
        {
            File.AppendAllText(_logFilePath, logMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write to PaperTrades.log");
        }
    }

    public Task OnCandleClosed(Candle candle)
    {
        lock (_candles)
        {
            // 1. STORE candle
            _candles.Add(candle);

            if (_candles.Count > _maxCandles)
                _candles.RemoveAt(0);

            // 2. NOT ENOUGH DATA → skip
            if (_candles.Count < 30)
                return Task.CompletedTask;

            // Map internal position state to expected Domain model position if strategy requires it
            var strategyPosition = new Position
            {
                IsOpen = _state.IsOpen,
                EntryPrice = _state.EntryPrice,
                Quantity = _state.BtcBalance,
                EntryTime = _state.EntryTime
            };

            // 3. ASK STRATEGY
            var signal = _strategy.Evaluate(_candles, strategyPosition);

            if (signal == null)
                return Task.CompletedTask;

            // 4. EXECUTE TRADE
            if (signal.Type == "BUY" && !_state.IsOpen)
            {
                var entryPrice = signal.Price;
                var totalAvailableUsd = _state.UsdBalance;
                
                // Calculate max BTC we can buy with available USD, accounting for 0.1% fee
                // Net USD needed = (Qty * Price) + (Qty * Price * FeePercent) = Qty * Price * (1 + FeePercent)
                // Qty = TotalUsd / (Price * (1 + FeePercent))
                
                var maxGrossValue = totalAvailableUsd / (1 + _tradingFeePercent);
                var quantity = Math.Round(maxGrossValue / entryPrice, 6);
                
                if (quantity <= 0) return Task.CompletedTask;
                
                var grossValue = quantity * entryPrice;
                var fee = grossValue * _tradingFeePercent;
                var netUsdCost = grossValue + fee;

                // Update state
                _state.UsdBalance -= netUsdCost;
                _state.BtcBalance += quantity;
                _state.IsOpen = true;
                _state.EntryPrice = entryPrice;
                _state.EntryTime = DateTime.UtcNow;

                var trade = new PaperTrade
                {
                    Side = "BUY",
                    Quantity = quantity,
                    ExecutionPrice = entryPrice,
                    GrossValue = grossValue,
                    Fee = fee,
                    NetValue = grossValue, // BTC value
                    Timestamp = DateTime.UtcNow,
                    Reason = signal.Reason
                };
                _state.TradeHistory.Add(trade);
                SaveState();
                LogTrade(trade);
            }
            else if (signal.Type == "SELL" && _state.IsOpen)
            {
                var exitPrice = signal.Price;
                var quantity = _state.BtcBalance;
                
                var grossProceeds = quantity * exitPrice;
                var fee = grossProceeds * _tradingFeePercent;
                var netUsdReceived = grossProceeds - fee;
                
                // Realized P/L calculation
                var initialCost = quantity * _state.EntryPrice;
                var realizedPnL = netUsdReceived - initialCost;

                // Update state
                _state.UsdBalance += netUsdReceived;
                _state.BtcBalance = 0;
                _state.IsOpen = false;
                _state.EntryPrice = 0;
                _state.EntryTime = null;

                var trade = new PaperTrade
                {
                    Side = "SELL",
                    Quantity = quantity,
                    ExecutionPrice = exitPrice,
                    GrossValue = grossProceeds,
                    Fee = fee,
                    NetValue = netUsdReceived,
                    Timestamp = DateTime.UtcNow,
                    Reason = $"{signal.Reason} (Realized P/L: {realizedPnL:C})"
                };
                _state.TradeHistory.Add(trade);
                SaveState();
                LogTrade(trade);
            }

            return Task.CompletedTask;
        }
    }
}
