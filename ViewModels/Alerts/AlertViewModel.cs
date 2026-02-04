using System.ComponentModel.DataAnnotations;

namespace dotnetApp.ViewModels.Alerts;

public class AlertCreateViewModel
{
    
    [Display(Name = "Symbol")]
    public required string Symbol { get; set; }

    [Display(Name = "Target Price")]
    [DataType(DataType.Currency)]
    public decimal TargetPrice { get; set; }

    [Display(Name = "Is Above?")]
    public bool IsAbove { get; set; }

    public List<Stocks> StockNames { get; set; } = new();

}

public class AlertEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Target Price")]
    [DataType(DataType.Currency)]
    public decimal TargetPrice { get; set; }

    [Display(Name = "Is Above?")]
    public bool IsAbove { get; set; }

    public List<Stocks> StockNames { get; set; } = new();

}

public class AlertsListViewModel
{
    public List<Alert> Alerts { get; set; } = new();
    public List<Stocks> StockNames { get; set; } = new();
}
