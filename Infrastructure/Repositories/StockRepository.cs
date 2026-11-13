using System.Threading.Tasks;
using dotnetApp.Application.Dtos;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Infrastructure.Repositories;

public class StockRepository
{
    private readonly AppDbContext _db;

    public StockRepository(AppDbContext db) => _db = db;

    #region Stocks

    public Task<Stock?> GetBySymbolAsync(string symbol)
        => _db.Stocks.FirstOrDefaultAsync(s => s.Symbol == symbol);

    public Task<List<Stock>> GetBySymbolsAsync(IEnumerable<string> symbols)
        => _db.Stocks.Where(s => symbols.Contains(s.Symbol)).ToListAsync();

    public Task<List<Stock>> GetAllStockNamesAsync()
        => _db.Stocks
              .AsNoTracking()
              .OrderBy(s => s.Name)
              .Select(s => new Stock
              {
                  Id = s.Id,
                  Name = s.Name,
                  Symbol = s.Symbol
              })
              .ToListAsync();

    public Task AddAsync(Stock stock) => _db.Stocks.AddAsync(stock).AsTask();

    #endregion

    #region MarketStatus

    public Task<MarketStatus?> GetMarketStatusAsync()
        => _db.MarketStatus.FirstOrDefaultAsync();

    public async Task AddAsync(MarketStatus data)
    {
        var entity = await this.GetMarketStatusAsync();
        bool isTradingDay = IsTradingDay(entity, data.IsOpen);

        if (entity == null)
        {
            entity = new MarketStatus
            {
                Id = 1
            };

            _db.MarketStatus.Add(entity);
        }

        entity.IsTradingDay = isTradingDay;
        entity.IsOpen = data.IsOpen;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
    
    private static readonly TimeZoneInfo Colombo = ResolveColombo();
    private static readonly TimeSpan OpenGrace = new(9, 45, 0); // CSE opens 09:30

    /// <summary>
    /// A weekday is only a trading day if the market actually opens (CSE has many public/poya holidays).
    /// Before 09:45 a weekday is assumed to be a trading day; after that it counts only if CSE reported
    /// the market open at some point today.
    /// </summary>
    private static bool IsTradingDay(MarketStatus? previous, bool isOpenNow)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Colombo);
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (isOpenNow) return true;
        if (now.TimeOfDay < OpenGrace) return true;

        // Market closed now: keep "trading day" only if it was confirmed earlier today (after 09:45 or while open).
        if (previous is null || !previous.IsTradingDay) return false;
        var prev = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(previous.UpdatedAt, DateTimeKind.Utc), Colombo);
        return prev.Date == now.Date && (previous.IsOpen || prev.TimeOfDay >= OpenGrace);
    }

    private static TimeZoneInfo ResolveColombo()
    {
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("LK", TimeSpan.FromHours(5.5), "Colombo", "Colombo");
    }

    #endregion

    #region MarketIndices

    public Task<MarketIndices?> GetMarketIndexAsync(MarketIndexType indexType)
        => _db.MarketIndices.FirstOrDefaultAsync(m => m.IndexType == indexType);

    public async Task AddAsync(MarketIndices data)
    {

        var entity = await this.GetMarketIndexAsync(data.IndexType);

        if (entity == null)
        {
            entity = new MarketIndices { IndexType = data.IndexType };
            await _db.MarketIndices.AddAsync(entity);
        }

        entity.IndexType = data.IndexType;
        entity.Value = data.Value;
        entity.HighValue = data.HighValue;
        entity.LowValue = data.LowValue;
        entity.Change = data.Change;
        entity.Percentage = data.Percentage;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

    }

    #endregion

    #region SaveChanges

    public Task SaveChangesAsync() => _db.SaveChangesAsync();

    #endregion

    public async Task<List<Stock>> GetStockTickersAsync()
    {
        return await _db.Stocks
            .AsNoTracking()
            .OrderByDescending(s => s.PercentageChange) // Top gainers first
            .Take(50)
            .ToListAsync();
    }

    public async Task<List<StockIntraDay>?> GetIntraDayValues()
    {
        return await _db.Stocks
            .AsNoTracking()
            .Select(s => new StockIntraDay
            {
                symbol = s.Symbol,
                name = s.Name,
                price = s.Price > 0 ? s.Price : s.ClosingPrice,
                high = s.High,
                low = s.Low,
                change = s.Change,
                percentage = s.PercentageChange,
                volatility = s.Low > 0 ? ((s.High - s.Low) / s.Low) * 100 : 0
            })
            .OrderByDescending(s => Math.Abs(s.percentage))
            .Take(50)
            .ToListAsync();
    }
}