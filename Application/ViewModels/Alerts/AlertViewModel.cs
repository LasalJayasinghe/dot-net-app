using System.ComponentModel.DataAnnotations;

namespace dotnetApp.Application.ViewModels.Alerts;

public class AlertCreateViewModel
{
    
    [Display(Name = "Symbol")]
    public required string Symbol { get; set; }

    [Display(Name = "Target Price")]
    [DataType(DataType.Currency)]
    public decimal TargetPrice { get; set; }

    [Display(Name = "Is Above?")]
    public bool IsAbove { get; set; }

    [Display(Name = "Is Active?")]
    public bool IsActive { get; set; } = true;

    /// <summary>Notify once, then switch off (default). False = notify on every pass while true.</summary>
    public bool FireOnce { get; set; } = true;

    public List<Stock> StockNames { get; set; } = new();

}

public class AlertEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Target Price")]
    [DataType(DataType.Currency)]
    public decimal TargetPrice { get; set; }

    [Display(Name = "Is Above?")]
    public bool IsAbove { get; set; }

    [Display(Name = "Is Active?")]
    public bool IsActive { get; set; } = true;

    /// <summary>Null leaves the alert's setting unchanged (older clients don't send it).</summary>
    public bool? FireOnce { get; set; }

    public List<Stock> StockNames { get; set; } = new();

}

public class AlertsListViewModel
{
    public List<Alert> Alerts { get; set; } = new();
    public List<Stock> StockNames { get; set; } = new();
}
