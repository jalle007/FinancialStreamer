namespace FinancialStreamer.Core.Models
{
    public class PriceUpdate
    {
        public string Symbol { get; set; } = string.Empty;
        public double? Price { get; set; }
        public DateTime? Timestamp { get; set; }
    }
}
