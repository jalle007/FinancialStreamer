using FinancialStreamer.Core.Models;

namespace FinancialStreamer.Core.Interfaces
{
    public interface IPriceDataProvider
    {
        Task<IEnumerable<FinancialInstrument>> GetInstrumentsAsync();
        Task<PriceUpdate?> GetPriceAsync(string symbol);
        Task SubscribeToPriceUpdatesAsync(string symbol, Action<PriceUpdate> onUpdate);
        Task UnsubscribeFromPriceUpdatesAsync(string symbol);
    }
}
