using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Core.Models;
using FinancialStreamer.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Restless.Tiingo.Core;
using Restless.Tiingo.Data;
using Restless.Tiingo.Socket.Data;

namespace FinancialStreamer.Infrastructure.Services
{ 
public class TiingoPriceDataProvider : IPriceDataProvider
{
    private readonly Restless.Tiingo.Client.TiingoClient _restClient;
    private readonly Restless.Tiingo.Socket.Client.TiingoClient _socketClient;
    private readonly ILogger<TiingoPriceDataProvider> _logger;
    private readonly TiingoSettings _settings;

    public TiingoPriceDataProvider(IOptions<TiingoSettings> settings, ILogger<TiingoPriceDataProvider> logger)
    {
        _settings = settings.Value;
        _restClient = Restless.Tiingo.Client.TiingoClient.Create(_settings.ApiKey);
        _socketClient = Restless.Tiingo.Socket.Client.TiingoClient.Create(_settings.ApiKey);
        _logger = logger;
    }

    public async Task<IEnumerable<FinancialInstrument>> GetInstrumentsAsync()
    {
        _logger.LogInformation("Fetching list of available financial instruments");
        var forexPairs = await _restClient.Forex.GetSupportedSymbolPairsAsync();
        return forexPairs.Select(item => new FinancialInstrument
        {
            Symbol = item.Ticker,
            Name = $"{item.BaseCurrency}/{item.QuoteCurrency}"
        });
    }

    public async Task<PriceUpdate?> GetPriceAsync(string symbol)
    {
        _logger.LogInformation($"Fetching price for {symbol}");
        var forexData = await _restClient.Forex.GetDataPointsAsync(new Restless.Tiingo.Core.ForexParameters
        {
            Tickers = new[] { new TickerPair(symbol.Substring(0, 3), symbol.Substring(3, 3)) },
            StartDate = DateTime.UtcNow.AddDays(-1),
            Frequency = FrequencyUnit.Day,
            FrequencyValue = 1
        });

        var latestData = forexData.LastOrDefault();
        if (latestData == null)
        {
            _logger.LogWarning($"No data available for {symbol}");
            return null;
        }

        var priceUpdate = new PriceUpdate
        {
            Symbol = symbol,
            Price = latestData.Close,
            Timestamp = latestData.Date
        };

        _logger.LogInformation($"Fetched price for {symbol}: {priceUpdate.Price} at {priceUpdate.Timestamp}");
        return priceUpdate;
    }

    public async Task SubscribeToPriceUpdatesAsync(string symbol, Action<PriceUpdate> onUpdate)
    {
        _logger.LogInformation($"Subscribing to price updates for {symbol}");
        await _socketClient.Forex.GetAsync(new Restless.Tiingo.Socket.Core.ForexParameters
        {
            Tickers = new[] { symbol }
        }, result =>
        {
            if (result is ForexQuoteMessage quote)
            {
                var priceUpdate = new PriceUpdate
                {
                    Symbol = quote.Ticker,
                    Price = quote.MidPrice,
                    Timestamp = quote.Timestamp
                };

                _logger.LogInformation($"Received price update for {symbol}: {priceUpdate.Price} at {priceUpdate.Timestamp}");
                onUpdate(priceUpdate);
            }
        });
    }
}

}