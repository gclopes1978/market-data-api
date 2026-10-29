namespace MarketData.Core;

public interface ICryptoCatalogProvider
{
    /// <summary>Returns every active cryptocurrency, ordered by market-cap rank.</summary>
    Task<MarketDataEnvelope<IReadOnlyList<CryptoAsset>>> GetAssetsAsync(
        CancellationToken ct);
}
