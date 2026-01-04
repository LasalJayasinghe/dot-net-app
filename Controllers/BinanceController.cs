using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers
{
    [Route("api/[controller]")]
    public class BinanceController : Controller
    {
        private readonly BinanceService _binanceService;

        public BinanceController(BinanceService binanceService)
        {
            _binanceService = binanceService;
        }

        [HttpGet]
        public IActionResult GetPrices()
        {
            return Ok(_binanceService.Prices);
        }
    }
}

