namespace MarketData.Core;

public interface IFxRateProvider
{
    /// <summary>
    /// Returns the exchange rate between two ISO 4217 currency codes,
    /// or <c>null</c> when the currency pair is unknown.
    /// </summary>
    Task<FxRate?> GetRateAsync(string baseCcy, string quoteCcy, CancellationToken ct);
}