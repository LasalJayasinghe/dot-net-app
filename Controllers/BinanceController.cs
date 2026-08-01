using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BinanceController : ControllerBase
    {
        private readonly BinanceService _binanceService;

        public BinanceController(BinanceService binanceService)
        {
            _binanceService = binanceService;
        }

    }
}

