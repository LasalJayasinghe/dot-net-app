public enum MarketIndexType { ASPI, SNP }

public class MarketIndices
{
    public int Id { get; set; }
    public MarketIndexType IndexType { get; set; } = MarketIndexType.ASPI;
    public decimal Value { get; set; }
    public decimal HighValue { get; set; }
    public decimal LowValue { get; set; }
    public decimal Change { get; set; }
    public decimal Percentage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}