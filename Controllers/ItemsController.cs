using dotnetApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers;

public class ItemsController : Controller
{
    public IActionResult Overview()
    {
        var item = new Item() { Id = 1, Name = "Sample Item", Price = 9.99M };
        return View(item);
    }
}