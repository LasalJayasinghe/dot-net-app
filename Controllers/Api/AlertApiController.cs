using dotnetApp.Application.ViewModels.Alerts;
using dotnetApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;

    public AlertApiController(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<IActionResult> Create(AlertCreateViewModel vm)
    {

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = new Alert
        {
            Symbol = vm.Symbol,
            TargetPrice = vm.TargetPrice,
            IsAbove = vm.IsAbove,
            IsActive = true,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetById),
            new { id = alert.Id },
            new { alert.Id }
        );
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, AlertEditViewModel vm)
    {

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = await _dbContext.Alerts
            .FirstOrDefaultAsync(a => a.Id == id && a.CreatedBy == user.Id);

        if (alert == null)
            return NotFound();

        alert.TargetPrice = vm.TargetPrice;
        alert.IsAbove = vm.IsAbove;

        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = await _dbContext.Alerts
            .FirstOrDefaultAsync(a => a.Id == id && a.CreatedBy == user.Id);

        if (alert == null)
            return NotFound();

        _dbContext.Alerts.Remove(alert);

        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var alert = await _dbContext.Alerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.CreatedBy == user.Id);

        if (alert == null)
            return NotFound();

        return Ok(alert);
    }
}
