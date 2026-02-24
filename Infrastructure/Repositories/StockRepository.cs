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

    public void Add(MarketStatus status) => _db.MarketStatus.Add(status);

    #endregion

    #region MarketIndices

    public Task<MarketIndices?> GetMarketIndexAsync(MarketIndexType indexType)
        => _db.MarketIndices.FirstOrDefaultAsync(m => m.IndexType == indexType);

    public void Add(MarketIndices marketIndex) => _db.MarketIndices.Add(marketIndex);

    #endregion

    #region SaveChanges

    public Task SaveChangesAsync() => _db.SaveChangesAsync();

    #endregion
}