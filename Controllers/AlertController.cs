using dotnetApp.Data;
using dotnetApp.ViewModels.Alerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers;

public class AlertController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;
    public AlertController(UserManager<ApplicationUser> userManager, AppDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AlertViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
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

    [HttpGet]
    public async Task<IActionResult> List()
    {
        // 1️⃣ Get logged-in user
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return Challenge(); 
        }

        var alerts = await _dbContext.Alerts
            .AsNoTracking()                 
            .Where(a => a.CreatedBy == user.Id && a.IsActive == true)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return View(alerts);
    }

}