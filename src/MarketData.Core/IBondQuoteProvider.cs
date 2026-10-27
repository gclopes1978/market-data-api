namespace MarketData.Core;

public interface IBondQuoteProvider
{
    Task<MarketDataEnvelope<BondQuote>?> GetQuoteAsync(
        string market,
        string identifier,
        CancellationToken ct);
}
