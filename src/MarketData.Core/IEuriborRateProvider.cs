namespace MarketData.Core;

public interface IEuriborRateProvider
{
    /// <summary>
    /// Returns the most recent monthly-average Euribor observation for the given tenor (<c>1M</c>, <c>3M</c>, <c>6M</c> or <c>12M</c>),
    /// or <c>null</c> when the upstream has no observation for it.
    /// </summary>
    Task<MarketDataEnvelope<EuriborRate>?> GetRateAsync(
        string tenor, CancellationToken ct);
}