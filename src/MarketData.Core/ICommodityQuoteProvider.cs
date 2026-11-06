namespace MarketData.Core;

public interface ICommodityQuoteProvider
{
    /// <summary>
    /// Returns the latest quote of a commodity (e.g. <c>GOLD</c>, <c>BRENT</c>),
    /// or <c>null</c> when the symbol is unknown or the upstream has no data for it.
    /// </summary>
    Task<MarketDataEnvelope<CommodityQuote>?> GetQuoteAsync(
        string symbol,
        CancellationToken ct);
}
