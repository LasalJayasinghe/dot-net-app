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

public class MarketStatusDto
{
    public string status { get; set; } = default!;
}

public class StockTopDto
{
    public string symbol { get; set; } = default!;
    public decimal price { get; set; }
    public decimal change { get; set; }
    public decimal changePercentage { get; set; }
}

public class StockIndicesDto
{
    public decimal value { get; set; }
    public decimal highValue { get; set; }
    public decimal lowValue { get; set; }
    public decimal change { get; set; }
    public decimal percentage { get; set; }
}