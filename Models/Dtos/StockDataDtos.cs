using System.Text.Json.Serialization;

namespace dotnetApp.Models.Dtos;

public class StockDataResponseDto
{
    [JsonPropertyName("reqSymbolInfo")]
    public ReqSymbolInfoDto ReqSymbolInfo { get; set; } = null!;
}

// Nested DTO
public class ReqSymbolInfoDto
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = null!;

    [JsonPropertyName("closingPrice")]
    public decimal ClosingPrice { get; set; }

    [JsonPropertyName("lastTradedPrice")]
    public decimal LastTradedPrice { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal PreviousClose { get; set; }

    [JsonPropertyName("highTrade")]
    public decimal High { get; set; }

    [JsonPropertyName("lowTrade")]
    public decimal Low { get; set; }

    [JsonPropertyName("changePercentage")]
    public decimal PercentageChange { get; set; }

    [JsonPropertyName("change")]
    public decimal Change { get; set; }

}

public class TradeSummaryResponseDto
{
    public List<TradeSummaryItemDto> ReqTradeSummery { get; set; } = [];
}

public class TradeSummaryItemDto
{
    public string Symbol { get; set; } = default!;
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal ClosingPrice { get; set; }
    public decimal PercentageChange { get; set; }
    public decimal Change { get; set; }
}
