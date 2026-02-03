using System.ComponentModel.DataAnnotations;

namespace dotnetApp.ViewModels.Alerts;

public class AlertViewModel
{
    public int Id { get; set; }
    
    [Display(Name = "Symbol")]
    public string Symbol { get; set; }

    [Display(Name = "Target Price")]
    [DataType(DataType.Currency)]
    public decimal TargetPrice { get; set; }

    [Display(Name = "Is Above?")]
    public bool IsAbove { get; set; }

    public List<Stocks> StockNames { get; set; } = new();

}