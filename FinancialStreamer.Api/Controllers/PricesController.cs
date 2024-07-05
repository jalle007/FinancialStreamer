using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FinancialStreamer.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PricesController : ControllerBase
    {
        private readonly IPriceDataProvider _priceDataProvider;
        private readonly ILogger<PricesController> _logger;

        public PricesController(IPriceDataProvider priceDataProvider, ILogger<PricesController> logger)
        {
            _priceDataProvider = priceDataProvider;
            _logger = logger;
        }

        [HttpGet("instruments")]
        public async Task<ActionResult<IEnumerable<FinancialInstrument>>> GetInstruments()
        {
            _logger.LogInformation("Fetching list of available financial instruments");
            var instruments = await _priceDataProvider.GetInstrumentsAsync();
            if (instruments == null)
            {
                _logger.LogWarning($"No data available");
                return NotFound();
            }

            return Ok(instruments);
        }

        [HttpGet("{symbol}")]
        public async Task<ActionResult<PriceUpdate>> GetPrice(string symbol)
        {
            _logger.LogInformation($"Fetching current price for {symbol}");
            var price = await _priceDataProvider.GetPriceAsync(symbol);
            if (price == null)
            {
                _logger.LogWarning($"No price data available for {symbol}");
                return NotFound();
            }
            return Ok(price);
        }
    }
}
