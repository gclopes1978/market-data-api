namespace MarketData.Core;

public interface IBondQuoteProvider
{
    /// <summary>
    /// Returns the quote of one point of a government yield curve (e.g. <c>US10Y</c>, <c>EUR5Y</c>) for the given market,
    /// or <c>null</c> when the market or maturity is not served by the provider.
    /// </summary>
    Task<MarketDataEnvelope<BondQuote>?> GetQuoteAsync(
        string market,
        string identifier,
        CancellationToken ct);
}
