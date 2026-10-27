namespace MarketData.Core;

public interface IEquityQuoteProvider
{
    Task<MarketDataEnvelope<EquityQuote>?> GetQuoteAsync(
        string ticker,
        CancellationToken ct);
}
