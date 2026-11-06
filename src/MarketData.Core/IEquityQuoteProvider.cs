namespace MarketData.Core;

public interface IEquityQuoteProvider
{
    /// <summary>
    /// Returns the latest quote of a stock or ETF by ticker,
    /// or <c>null</c> when the ticker is unknown to the upstream provider.
    /// </summary>
    Task<MarketDataEnvelope<EquityQuote>?> GetQuoteAsync(
        string ticker,
        CancellationToken ct);
}
