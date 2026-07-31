using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/algorithms")]
[Authorize]
public class AlgorithmsApiController : ControllerBase
{
    private static readonly Dictionary<string, AlgorithmState> _states = new()
    {
        ["ema"] = new AlgorithmState
        {
            Id = "ema",
            Name = "EMA Strategy",
            Description = "Exponential moving average crossover on 1H candles.",
            Status = "RUNNING",
            TotalPnl = 4821.34m,
            TotalPnlPct = 18.7m,
            WinRate = 64.2m,
            ActiveSignals = 3,
            TotalTrades = 142,
            CurrentSignal = "BUY",
            LastUpdated = DateTime.UtcNow.AddMinutes(-2)
        },
        ["rsi"] = new AlgorithmState
        {
            Id = "rsi",
            Name = "RSI Breakout",
            Description = "RSI divergence with volume confirmation.",
            Status = "RUNNING",
            TotalPnl = -612.50m,
            TotalPnlPct = -2.4m,
            WinRate = 48.1m,
            ActiveSignals = 1,
            TotalTrades = 87,
            CurrentSignal = "SELL",
            LastUpdated = DateTime.UtcNow.AddMinutes(-7)
        }
    };

    [HttpGet]
    public IActionResult List()
    {
        return Ok(_states.Values.Select(ToSummary));
    }

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        if (!_states.TryGetValue(id, out var state))
            return NotFound(new { message = "Algorithm not found" });

        var detail = new
        {
            id = state.Id,
            name = state.Name,
            description = state.Description,
            status = state.Status,
            totalPnl = state.TotalPnl,
            totalPnlPct = state.TotalPnlPct,
            winRate = state.WinRate,
            activeSignals = state.ActiveSignals,
            totalTrades = state.TotalTrades,
            currentSignal = state.CurrentSignal,
            lastUpdated = state.LastUpdated,
            signals = BuildSignals(state.Id),
            trade = new
            {
                entryPrice = state.Id == "ema" ? 67100.0m : 3450.25m,
                stopLoss = state.Id == "ema" ? 66250.0m : 3380.0m,
                takeProfit = state.Id == "ema" ? 69000.0m : 3620.0m,
                strategy = state.Id == "ema"
                    ? "Long when 9-EMA crosses above 21-EMA with rising volume. Exit on opposite crossover or stop-loss hit."
                    : "Short when RSI(14) breaks below 30 after a bearish divergence. Confirmation requires volume spike > 1.5x average.",
            },
            history = BuildHistory(state.Id),
        };

        return Ok(detail);
    }

    [HttpPost("{id}/toggle")]
    public IActionResult Toggle(string id)
    {
        if (!_states.TryGetValue(id, out var state))
            return NotFound(new { message = "Algorithm not found" });

        state.Status = state.Status == "RUNNING" ? "STOPPED" : "RUNNING";
        state.LastUpdated = DateTime.UtcNow;

        return Ok(ToSummary(state));
    }

    private static object ToSummary(AlgorithmState state)
    {
        return new
        {
            id = state.Id,
            name = state.Name,
            description = state.Description,
            status = state.Status,
            totalPnl = state.TotalPnl,
            totalPnlPct = state.TotalPnlPct,
            winRate = state.WinRate,
            activeSignals = state.ActiveSignals,
            totalTrades = state.TotalTrades,
            currentSignal = state.CurrentSignal,
            lastUpdated = state.LastUpdated,
        };
    }

    private static IEnumerable<object> BuildSignals(string id)
    {
        var pairs = new[] { "BTC/USDT", "ETH/USDT", "SOL/USDT", "BNB/USDT", "XRP/USDT" };
        var types = new[] { "BUY", "SELL", "HOLD" };

        return pairs.Select((pair, i) => new
        {
            id = $"{id}-sig-{i}",
            pair,
            signal = types[(i + (id == "rsi" ? 1 : 0)) % 3],
            price = new[] { 67234.12m, 3421.55m, 162.84m, 612.40m, 0.5821m }[i],
            confidence = 60 + ((i * 7 + (id == "rsi" ? 3 : 11)) % 35),
            time = DateTime.UtcNow.AddMinutes(-(i * 8 + 3))
        });
    }

    private static IEnumerable<object> BuildHistory(string id)
    {
        return Enumerable.Range(0, 8).Select(i =>
        {
            var win = (i + (id == "rsi" ? 1 : 0)) % 3 != 0;
            var entry = 100m + i * 12.4m;
            var exit = win ? entry * 1.024m : entry * 0.987m;
            var pnl = exit - entry;

            return new
            {
                id = $"{id}-tr-{i}",
                pair = new[] { "BTC/USDT", "ETH/USDT", "SOL/USDT", "BNB/USDT" }[i % 4],
                entryPrice = entry,
                exitPrice = exit,
                pnl,
                pnlPct = entry == 0 ? 0 : (pnl / entry) * 100,
                durationMinutes = 25 + i * 17,
                result = win ? "WIN" : "LOSS",
                closedAt = DateTime.UtcNow.AddHours(-(i + 1))
            };
        });
    }

    private sealed class AlgorithmState
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string Status { get; set; }
        public decimal TotalPnl { get; set; }
        public decimal TotalPnlPct { get; set; }
        public decimal WinRate { get; set; }
        public int ActiveSignals { get; set; }
        public int TotalTrades { get; set; }
        public required string CurrentSignal { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
