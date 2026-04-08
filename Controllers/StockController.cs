using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using dotnetApp.Infrastructure.Repositories;

namespace dotnetApp.Controllers
{
    [Authorize]
    public class StockController : Controller
    {
        private readonly StockRepository _stockRepository;
        public StockController(StockRepository stockRepository)
        {
            _stockRepository = stockRepository;
        }

        public async Task<IActionResult> Index()
        {
            var AspiData = await _stockRepository.GetMarketIndexAsync(MarketIndexType.ASPI);
            var SnpData = await _stockRepository.GetMarketIndexAsync(MarketIndexType.SNP);
            var marketStatus = await _stockRepository.GetMarketStatusAsync();

            // Pass a model to the view
            var model = new MarketViewModel
            {
                Aspi = AspiData,
                Snp = SnpData,
                Status = marketStatus
            };

            return View(model);
        }
    }
}
