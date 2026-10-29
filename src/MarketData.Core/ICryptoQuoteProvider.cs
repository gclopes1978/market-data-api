namespace MarketData.Core;

public interface ICryptoQuoteProvider
{
    /// <summary>
    /// Returns the latest quote of a cryptocurrency in the given fiat currency,
    /// or <c>null</c> when the asset (or the currency) is unknown.
    /// </summary>
    Task<MarketDataEnvelope<CryptoQuote>?> GetQuoteAsync(
        string id,
        string currency,
        CancellationToken ct);
}
