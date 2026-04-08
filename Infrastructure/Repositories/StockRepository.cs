using System.Threading.Tasks;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Infrastructure.Repositories;

public class StockRepository
{
    private readonly AppDbContext _db;

    public StockRepository(AppDbContext db) => _db = db;

    #region Stocks

    public Task<Stocks?> GetBySymbolAsync(string symbol)
        => _db.Stocks.FirstOrDefaultAsync(s => s.Symbol == symbol);

    public Task<List<Stocks>> GetBySymbolsAsync(IEnumerable<string> symbols)
        => _db.Stocks.Where(s => symbols.Contains(s.Symbol)).ToListAsync();

    public Task<List<Stocks>> GetAllStockNamesAsync()
        => _db.Stocks
              .AsNoTracking()
              .OrderBy(s => s.Name)
              .Select(s => new Stocks
              {
                  Id = s.Id,
                  Name = s.Name,
                  Symbol = s.Symbol
              })
              .ToListAsync();

    public Task AddAsync(Stocks stock) => _db.Stocks.AddAsync(stock).AsTask();

    #endregion

    #region MarketStatus

    public Task<MarketStatus?> GetMarketStatusAsync()
        => _db.MarketStatus.FirstOrDefaultAsync();

    public async Task AddAsync(MarketStatus data)
    {
        var entity = await this.GetMarketStatusAsync();
        bool IsTradingDay = DateTime.UtcNow.DayOfWeek != DayOfWeek.Saturday && DateTime.UtcNow.DayOfWeek != DayOfWeek.Sunday;

        if (entity == null)
        {
            entity = new MarketStatus { Id = 1 };
            _db.MarketStatus.Add(entity);
        }

        entity.IsTradingDay = IsTradingDay;
        entity.IsOpen = data.IsOpen;
        entity.UpdatedAt = DateTime.UtcNow;

        _db.MarketStatus.Update(entity);
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
            if(data.IndexType == MarketIndexType.ASPI)
            {
                entity = new MarketIndices();
            }
            else if(data.IndexType == MarketIndexType.SNP)
            {
                entity = new MarketIndices();
            }
            _db.Add(entity);
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
}