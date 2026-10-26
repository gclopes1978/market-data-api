namespace MarketData.Core;

public interface IFxRateProvider
{
    Task<FxRate?> GetRateAsync(string baseCcy, string quoteCcy, CancellationToken ct);
}