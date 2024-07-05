using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Core.Models;
using FinancialStreamer.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Restless.Tiingo.Core;
using Restless.Tiingo.Data;
using Restless.Tiingo.Socket.Client;
using Restless.Tiingo.Socket.Data;
using System.Collections.Concurrent;

namespace FinancialStreamer.Infrastructure.Services
{
    /// <summary>
    /// Provides price data using the Tiingo API.
    /// </summary>
    public class TiingoPriceDataProvider : IPriceDataProvider
    {
        private readonly Restless.Tiingo.Client.TiingoClient _restClient;
        private readonly Restless.Tiingo.Socket.Client.TiingoClient _socketClient;
        private readonly ILogger<TiingoPriceDataProvider> _logger;
        private readonly TiingoSettings _settings;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;

        /// <summary>
        /// Initializes a new instance of the <see cref="TiingoPriceDataProvider"/> class.
        /// </summary>
        /// <param name="settings">The Tiingo settings containing the API key.</param>
        /// <param name="logger">The logger instance.</param>
        public TiingoPriceDataProvider(IOptions<TiingoSettings> settings, ILogger<TiingoPriceDataProvider> logger)
        {
            _settings = settings.Value;
            _restClient = Restless.Tiingo.Client.TiingoClient.Create(_settings.ApiKey);
            _socketClient = Restless.Tiingo.Socket.Client.TiingoClient.Create(_settings.ApiKey);
            _logger = logger;
            _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        }

        /// <summary>
        /// Gets a list of available financial instruments.
        /// </summary>
        /// <returns>A list of financial instruments.</returns>
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

        /// <summary>
        /// Gets the current price of a specific financial instrument.
        /// </summary>
        /// <param name="symbol">The symbol of the financial instrument.</param>
        /// <returns>A <see cref="PriceUpdate"/> containing the current price and timestamp.</returns>
        public async Task<PriceUpdate?> GetPriceAsync(string symbol)
        {
            var forexData = await _restClient.Forex.GetDataPointsAsync(new ForexParameters
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

        /// <summary>
        /// Subscribes to live price updates for a specific financial instrument. 
        /// </summary>
        /// <param name="symbol">The symbol of the financial instrument.</param>
        /// <param name="onUpdate">The action to perform on each price update.</param>
        public async Task SubscribeToPriceUpdatesAsync(string symbol, Action<PriceUpdate> onUpdate)
        {
            _logger.LogInformation($"[{DateTime.UtcNow}] Subscribing to price updates for {symbol}");

            var cancellationTokenSource = _cancellationTokens.GetOrAdd(symbol, _ =>
            {
                return new CancellationTokenSource();
            });

            if (_cancellationTokens[symbol].Token.IsCancellationRequested)
            {
                _logger.LogInformation($"[{DateTime.UtcNow}] CancellationToken for {symbol} was cancelled, creating a new one");
                _cancellationTokens[symbol] = new CancellationTokenSource();
            }

            if (_cancellationTokens[symbol] == cancellationTokenSource)
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        await _socketClient.Forex.GetAsync(new Restless.Tiingo.Socket.Core.ForexParameters
                        {
                            Tickers = new[] { symbol },
                            Threshold = Restless.Tiingo.Socket.Core.ForexThreshold.LastQuote
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

                                _logger.LogInformation($"[{DateTime.UtcNow}] Received price update for {symbol}: {priceUpdate.Price} at {priceUpdate.Timestamp}");
                                onUpdate(priceUpdate);
                            }
                        });

                        _logger.LogInformation($"[{DateTime.UtcNow}] Completed Forex.GetAsync for {symbol}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[{DateTime.UtcNow}] Error during Forex.GetAsync for {symbol}");
                    }
                }, _cancellationTokens[symbol].Token);
            }
        }


        /// <summary>
        /// Unsubscribes from live price updates for a specific financial instrument.
        /// </summary>
        /// <param name="symbol">The symbol of the financial instrument.</param>
        public async Task UnsubscribeFromPriceUpdatesAsync(string symbol)
        {
            _logger.LogInformation($"Unsubscribing from price updates for {symbol}");

            if (_cancellationTokens.TryRemove(symbol, out var cancellationTokenSource))
            {
                cancellationTokenSource.Cancel();
                _logger.LogInformation($"Successfully unsubscribed from price updates for {symbol}");
            }
            else
            {
                _logger.LogWarning($"No active subscription found for {symbol} to unsubscribe");
            }

            await Task.CompletedTask;
        }
    }
}
