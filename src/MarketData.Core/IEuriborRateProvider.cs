namespace MarketData.Core;

public interface IEuriborRateProvider
{
    Task<MarketDataEnvelope<EuriborRate>?> GetRateAsync(
        string tenor, CancellationToken ct);
}