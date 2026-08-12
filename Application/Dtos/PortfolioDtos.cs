// ─────────────────────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class CreatePortfolioRequest
{
    public string Name { get; set; } = null!;
    public PortfolioType Type { get; set; }         // Stocks or Crypto
    public string BaseCurrency { get; set; } = "LKR"; // "LKR" or "USDT"
    public string? Description { get; set; }
}

public class UpdatePortfolioRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class AddHoldingRequest
{
    public string Symbol { get; set; } = null!;      // "ABAN.N0000" or "BTCUSDT"
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }     // In portfolio BaseCurrency
    public string? Notes { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

public class HoldingDto
{
    public int Id { get; set; }
    public string Symbol { get; set; } = null!;
    public string AssetType { get; set; } = null!;    // "Stock" or "Crypto"
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentValue { get; set; }         // Quantity * CurrentPrice
    public decimal ProfitLoss { get; set; }           // CurrentValue - (Quantity * AverageBuyPrice)
    public decimal ProfitLossPercent { get; set; }
    public string? Notes { get; set; }
}

public class PortfolioDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;         // "Stocks" or "Crypto"
    public string BaseCurrency { get; set; } = null!;
    public string? Description { get; set; }
    public decimal TotalValue { get; set; }           // Sum of all holding CurrentValues
    public decimal TotalCost { get; set; }            // Sum of all Quantity * AverageBuyPrice
    public decimal TotalProfitLoss { get; set; }
    public decimal TotalProfitLossPercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<HoldingDto> Holdings { get; set; } = new();
}

public class CurrencyExchangeRateDto
{
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ParsedHoldingDto
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AverageBuyPrice { get; set; }
}

public class PortfolioSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string BaseCurrency { get; set; } = null!;
    public string? Description { get; set; }
    public decimal TotalValue { get; set; }           // Native currency total
    public decimal TotalProfitLoss { get; set; }
    public decimal TotalProfitLossPercent { get; set; }
    public int HoldingCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NetWorthOverviewDto
{
    public decimal TotalNetWorthLkr { get; set; }
    public decimal TotalNetWorthUsdt { get; set; }
    public decimal TotalProfitLossLkr { get; set; }
    public decimal TotalProfitLossUsdt { get; set; }
    public decimal UsdtToLkrRate { get; set; }
    public decimal LkrToUsdtRate { get; set; }
    public List<PortfolioSummaryDto> Portfolios { get; set; } = new();
}
