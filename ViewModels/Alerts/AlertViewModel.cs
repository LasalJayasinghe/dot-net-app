using System.ComponentModel.DataAnnotations;

namespace dotnetApp.ViewModels.Alerts;

public class AlertViewModel
{
    [Required]
    [Display(Name ="Symbol")]
    public required string Symbol { get; set;}

    [Required]
    [Display(Name ="Target Price")]
    [DataType(DataType.Currency)]
    public decimal TargetPrice { get; set; }

    [Required]
    [Display(Name = "Is Above?")]
    public bool IsAbove { get; set; }

}