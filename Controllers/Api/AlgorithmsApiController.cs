using dotnetApp.Application.Trading.Engine;
using dotnetApp.Application.Trading.Market;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

/// <summary>
/// Exposes every paper-trading bot run by <see cref="TradingEngine"/> to the Algorithms view.
/// State is read in-memory from the engine (persisted per bot as JSON) — no database involved.
/// All P/L figures are NET of Binance trading fees (entry + exit); open positions are valued as if sold now.
/// </summary>
[ApiController]
[Route("api/algorithms")]
[Authorize]
public class AlgorithmsApiController : ControllerBase
{
    private const int HistoryLimit = 100;

    private readonly TradingEngine _engine;
    private readonly BinanceService _binance;

    public AlgorithmsApiController(TradingEngine engine, BinanceService binance)
    {
        _engine = engine;
        _binance = binance;
    }

    [HttpGet]
    public IActionResult List() =>
        Ok(_engine.Bots.Select(bot => bot.Read((state, strategy, fee) => BuildSummary(state, strategy, fee))));

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var bot = _engine.Find(id);
        if (bot is null) return NotFound(new { message = "Algorithm not found" });

        return Ok(bot.Read((state, strategy, fee) =>
        {
            var s = BuildSummary(state, strategy, fee);
            return new
            {
                s.id, s.name, s.description, s.category, s.pair, s.timeframe, s.status,
                s.totalPnl, s.totalPnlPct, s.winRate, s.activeSignals, s.totalTrades, s.currentSignal, s.lastUpdated,
                s.totalFees, s.feeRate, s.realizedPnl, s.unrealizedPnl, s.equity, s.cash, s.initialBalance,
                indicators = strategy.Indicators(),
                signals = BuildSignals(state),
                trade = BuildOpenTrade(state, strategy, fee),
                positions = state.Holdings.Select(kv => BuildPosition(kv.Key, kv.Value, fee)).ToList(),
                history = BuildHistory(state),
            };
        }));
    }

    // Anyone can sign up as a User, so starting/stopping trading is restricted to Admin.
    [HttpPost("{id}/toggle")]
    [Authorize(Roles = "Admin")]
    public IActionResult Toggle(string id)
    {
        var bot = _engine.Find(id);
        if (bot is null) return NotFound(new { message = "Algorithm not found" });

        var enabled = bot.Read((state, _, _) => state.IsEnabled);
        bot.SetEnabled(!enabled);
        return Ok(bot.Read((state, strategy, fee) => BuildSummary(state, strategy, fee)));
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private sealed record Summary(
        string id, string name, string description, string category, string pair, string timeframe, string status,
        decimal totalPnl, decimal totalPnlPct, decimal winRate, int activeSignals, int totalTrades,
        string currentSignal, DateTime lastUpdated,
        decimal totalFees, decimal feeRate, decimal realizedPnl, decimal unrealizedPnl, decimal equity,
        decimal cash, decimal initialBalance);

    private Summary BuildSummary(BotState state, ITradingStrategy strategy, decimal feeRate)
    {
        // Liquidation value: market value minus the exit fee still to be paid.
        decimal holdingsValue = 0, unrealized = 0;
        foreach (var (symbol, h) in state.Holdings)
        {
            var value = h.Quantity * Price(symbol, h) * (1 - feeRate);
            holdingsValue += value;
            unrealized += value - h.CostBasis;
        }

        var equity = state.Cash + holdingsValue;
        var totalPnl = equity - state.InitialBalance;
        var closed = state.Wins + state.Losses;
        var lastSignal = state.Signals.Count > 0 ? state.Signals[^1] : null;

        return new Summary(
            id: strategy.Id,
            name: strategy.Name,
            description: strategy.Description,
            category: strategy.Category,
            pair: strategy.Pair,
            timeframe: strategy.Timeframe,
            status: state.IsEnabled ? "RUNNING" : "STOPPED",
            totalPnl: Math.Round(totalPnl, 2),
            totalPnlPct: state.InitialBalance == 0 ? 0 : Math.Round(totalPnl / state.InitialBalance * 100, 2),
            winRate: closed == 0 ? 0 : Math.Round((decimal)state.Wins / closed * 100, 1),
            activeSignals: state.Holdings.Count,
            totalTrades: closed,
            currentSignal: lastSignal?.Type is "BUY" or "SELL" ? lastSignal.Type : "HOLD",
            lastUpdated: state.LastEventTime ?? state.CreatedAt,
            totalFees: Math.Round(state.TotalFees, 4),
            feeRate: feeRate,
            realizedPnl: Math.Round(state.RealizedPnl, 2),
            unrealizedPnl: Math.Round(unrealized, 2),
            equity: Math.Round(equity, 2),
            cash: Math.Round(state.Cash, 2),
            initialBalance: state.InitialBalance);
    }

    private static IEnumerable<object> BuildSignals(BotState state) =>
        state.Signals
            .AsEnumerable()
            .Reverse()
            .Select((s, i) => new
            {
                id = $"{s.Time.Ticks}-{i}",
                pair = Symbols.ToPair(s.Symbol),
                signal = s.Type,
                price = s.Price,
                confidence = (int?)null,
                reason = s.Reason,
                executed = s.Executed,
                time = s.Time,
            })
            .ToList();

    private object? BuildOpenTrade(BotState state, ITradingStrategy strategy, decimal feeRate)
    {
        // Largest open position by cost (most strategies hold at most one).
        var open = state.Holdings.OrderByDescending(kv => kv.Value.CostBasis).FirstOrDefault();
        if (open.Value is null) return null;

        var p = BuildPosition(open.Key, open.Value, feeRate);
        return new
        {
            p.symbol, p.pair, p.entryPrice, p.breakEvenPrice, p.currentPrice, p.quantity, p.entryFee, p.entryTime,
            p.stopLoss, p.takeProfit, p.unrealizedPnl,
            strategy = $"{strategy.Description} P/L and break-even include the {feeRate * 100:0.###}% Binance fee on entry and exit.",
        };
    }

    private sealed record PositionDto(
        string symbol, string pair, decimal entryPrice, decimal breakEvenPrice, decimal currentPrice, decimal quantity,
        decimal entryFee, DateTime entryTime, decimal? stopLoss, decimal? takeProfit, decimal unrealizedPnl);

    private PositionDto BuildPosition(string symbol, HoldingState h, decimal feeRate)
    {
        var price = Price(symbol, h);
        // Exit price at which proceeds after the exit fee cover cost + entry fee.
        var breakEven = h.Quantity == 0 ? 0 : h.CostBasis / (h.Quantity * (1 - feeRate));
        return new PositionDto(
            symbol, Symbols.ToPair(symbol),
            entryPrice: Math.Round(h.AvgEntryPrice, 6),
            breakEvenPrice: Math.Round(breakEven, 6),
            currentPrice: price,
            quantity: h.Quantity,
            entryFee: Math.Round(h.EntryFees, 4),
            entryTime: h.OpenedAt,
            stopLoss: h.StopLoss is { } sl ? Math.Round(sl, 6) : null,
            takeProfit: h.TakeProfit is { } tp ? Math.Round(tp, 6) : null,
            unrealizedPnl: Math.Round(h.Quantity * price * (1 - feeRate) - h.CostBasis, 2));
    }

    private static IEnumerable<object> BuildHistory(BotState state)
    {
        var rows = new List<object>(HistoryLimit);
        for (var i = state.Trades.Count - 1; i >= 0 && rows.Count < HistoryLimit; i--)
        {
            var t = state.Trades[i];
            if (t.Side != "SELL" || t.RealizedPnl is not { } pnl) continue;

            var cost = t.CostBasis ?? 0;
            rows.Add(new
            {
                id = t.Id,
                pair = Symbols.ToPair(t.Symbol),
                entryPrice = Math.Round(t.EntryPrice ?? 0, 6),
                exitPrice = t.Price,
                quantity = t.Quantity,
                pnl = Math.Round(pnl, 2),
                pnlPct = cost == 0 ? 0 : Math.Round(pnl / cost * 100, 2),
                fees = Math.Round((t.EntryFee ?? 0) + t.Fee, 4),
                durationMinutes = t.HoldMinutes ?? 0,
                result = pnl > 0 ? "WIN" : "LOSS",
                closedAt = t.Timestamp,
                reason = t.Reason,
            });
        }
        return rows;
    }

    private decimal Price(string symbol, HoldingState h) =>
        _binance.GetTicker(symbol) is { LastPrice: > 0 } t ? t.LastPrice
        : _engine.LastPrice(symbol) ?? h.AvgEntryPrice;
}
