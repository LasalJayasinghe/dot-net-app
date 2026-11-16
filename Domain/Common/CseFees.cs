namespace dotnetApp.Domain.Common;

/// <summary>
/// Colombo Stock Exchange transaction charges for retail trades, matching ATrad's calculations.
/// Average buy prices (ATrad exports / manual entry) are raw trade prices, so buy charges are added on top.
/// </summary>
public static class CseFees
{
    /// <summary>Charges on a purchase, as a fraction (1.12%): brokerage + CSE + CDS + SEC + STL.</summary>
    public const decimal BuyRate = 0.0112m;

    /// <summary>Charges on a sale, as a fraction (1.12%).</summary>
    public const decimal SellRate = 0.0112m;

    /// <summary>Total cost of a purchase including buy charges.</summary>
    public static decimal CostWithBuyFees(decimal grossBuyValue) => grossBuyValue * (1 + BuyRate);

    /// <summary>Value after selling charges (what you'd receive if sold now).</summary>
    public static decimal NetOfSale(decimal grossValue) => grossValue * (1 - SellRate);

    /// <summary>
    /// Break-even selling price (ATrad "BEP/BES"):
    /// (buy value + buy fees) / (quantity × (1 − sell fee rate)) = avg × (1 + BuyRate) / (1 − SellRate).
    /// Selling at this price returns exactly the total cost after all charges.
    /// </summary>
    public static decimal BreakEvenPrice(decimal averageBuyPrice) =>
        CostWithBuyFees(averageBuyPrice) / (1 - SellRate);
}
