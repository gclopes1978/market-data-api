namespace MarketData.Core;

public interface ICommodityQuoteProvider
{
    Task<MarketDataEnvelope<CommodityQuote>?> GetQuoteAsync(
        string symbol,
        CancellationToken ct);
}
