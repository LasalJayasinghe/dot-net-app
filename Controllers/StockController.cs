using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace dotnetApp.Controllers
{
    [Authorize]
    public class StockController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
