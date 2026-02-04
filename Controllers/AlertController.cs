using dotnetApp.Data;
using dotnetApp.ViewModels.Alerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers;

[Authorize]
public class AlertController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StockService _stockService;
    private readonly AppDbContext _dbContext;

    public AlertController(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        StockService stockService)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _stockService = stockService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var alerts = await _dbContext.Alerts
            .AsNoTracking()
            .Where(a => a.CreatedBy == user.Id)
            .OrderByDescending(a => a.IsActive)       // Active alerts first
            .ThenByDescending(a => a.CreatedAt)       // Newest alerts first
            .ToListAsync();


        var stockNames = await _stockService.GetAllStockNamesAsync();

        var vm = new AlertsListViewModel
        {
            Alerts = alerts,
            StockNames = stockNames
        };

        return View(vm);
    }
}
