using dotnetApp.Data;
using dotnetApp.ViewModels.Alerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers;

public class AlertController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StockService _stockService;
    private readonly AppDbContext _dbContext;
    public AlertController(UserManager<ApplicationUser> userManager, AppDbContext dbContext , StockService stockService)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _stockService = stockService;
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.StockNames = _stockService.GetAllStockNamesAsync().Result;
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AlertViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if( user == null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var alert = new Alert
        {
            Symbol = model.Symbol,
            TargetPrice = model.TargetPrice,
            IsAbove = model.IsAbove,
            IsActive = true,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction("Index", "Home");
    }
}