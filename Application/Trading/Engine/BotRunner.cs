using System.Text.Json;
using dotnetApp.Application.Models;

namespace dotnetApp.Application.Trading.Engine;

/// <summary>One strategy + its paper account + persistence. All access goes through <see cref="Sync"/>.</summary>
public sealed class BotRunner
{
    private static readonly JsonSerializerOptions FileJson = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger _logger;

    public BotRunner(ITradingStrategy strategy, decimal feeRate, decimal initialBalance, string directory, ILogger logger)
    {
        Strategy = strategy;
        _logger = logger;
        _path = Path.Combine(directory, $"{strategy.Id}.json");
        Account = new PaperAccount(new BotState { InitialBalance = initialBalance, Cash = initialBalance }, feeRate);
        Context = new StrategyContext(Account);
    }

    public ITradingStrategy Strategy { get; }
    public PaperAccount Account { get; }
    public StrategyContext Context { get; }
    public object Sync { get; } = new();

    public void Load(Func<BotState?>? legacyImport = null)
    {
        try
        {
            BotState? state = null;
            if (File.Exists(_path))
                state = JsonSerializer.Deserialize<BotState>(File.ReadAllText(_path));
            else if (legacyImport?.Invoke() is { } imported)
            {
                state = imported;
                _logger.LogInformation("Imported legacy paper-trading state into bot {Bot}.", Strategy.Id);
            }

            if (state is null) return;

            Account.State = state;
            if (!string.IsNullOrEmpty(state.StrategyState))
                Strategy.LoadState(state.StrategyState);

            if (!File.Exists(_path)) Save();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state for bot {Bot}. Starting fresh.", Strategy.Id);
        }
    }

    /// <summary>Atomic write (temp file + replace). Caller holds <see cref="Sync"/>.</summary>
    public void Save()
    {
        try
        {
            Account.State.StrategyState = Strategy.SaveState();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Account.State, FileJson));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state for bot {Bot}.", Strategy.Id);
        }
    }

    /// <summary>Runs a read-only projection under the bot's lock (no state copying).</summary>
    public T Read<T>(Func<BotState, ITradingStrategy, decimal, T> projection)
    {
        lock (Sync) return projection(Account.State, Strategy, Account.FeeRate);
    }

    public bool SetEnabled(bool enabled)
    {
        lock (Sync)
        {
            Account.State.IsEnabled = enabled;
            Save();
            return enabled;
        }
    }
}

/// <summary>Converts the original single-bot PaperTradingState.json into the new per-bot format.</summary>
public static class LegacyStateImporter
{
    public static BotState? TryImport(string path)
    {
        if (!File.Exists(path)) return null;

        var legacy = JsonSerializer.Deserialize<PaperTradingState>(File.ReadAllText(path));
        if (legacy is null) return null;

        var state = new BotState
        {
            InitialBalance = legacy.InitialBalance,
            Cash = legacy.UsdBalance,
            IsEnabled = legacy.IsEnabled,
            CreatedAt = legacy.TradeHistory.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow,
        };

        PaperTrade? openBuy = null;
        foreach (var t in legacy.TradeHistory.OrderBy(t => t.Timestamp))
        {
            state.TotalFees += t.Fee;
            var record = new TradeRecord
            {
                Id = t.Id,
                Symbol = t.Symbol,
                Side = t.Side,
                Quantity = t.Quantity,
                Price = t.ExecutionPrice,
                GrossValue = t.GrossValue,
                Fee = t.Fee,
                NetValue = t.Side == "BUY" ? t.GrossValue + t.Fee : t.NetValue,
                Timestamp = t.Timestamp,
                Reason = t.Reason,
            };

            if (t.Side == "BUY")
            {
                openBuy = t;
            }
            else if (t.Side == "SELL" && openBuy is not null)
            {
                var entryFee = t.EntryFee ?? openBuy.Fee;
                var costBasis = t.Quantity * openBuy.ExecutionPrice + entryFee;
                var pnl = t.RealizedPnl ?? t.NetValue - costBasis;
                record.EntryPrice = openBuy.ExecutionPrice;
                record.EntryFee = entryFee;
                record.CostBasis = costBasis;
                record.RealizedPnl = pnl;
                record.HoldMinutes = (int)Math.Max(0, (t.Timestamp - openBuy.Timestamp).TotalMinutes);
                state.RealizedPnl += pnl;
                if (pnl > 0) state.Wins++; else state.Losses++;
                openBuy = null;
            }

            state.Trades.Add(record);
        }

        if (legacy.IsOpen && legacy.BtcBalance > 0)
        {
            state.Holdings["BTCUSDT"] = new HoldingState
            {
                Quantity = legacy.BtcBalance,
                GrossCost = legacy.BtcBalance * legacy.EntryPrice,
                EntryFees = legacy.EntryFee > 0 ? legacy.EntryFee : openBuy?.Fee ?? 0,
                OpenedAt = legacy.EntryTime ?? openBuy?.Timestamp ?? DateTime.UtcNow,
            };
        }

        return state;
    }
}
