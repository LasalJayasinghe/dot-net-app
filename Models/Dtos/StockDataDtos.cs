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
}