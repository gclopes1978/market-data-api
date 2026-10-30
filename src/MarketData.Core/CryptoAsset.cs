namespace MarketData.Core;

/// <summary>
/// A cryptocurrency listed by the provider. <c>Id</c> is the provider identifier
/// (e.g. "btc-bitcoin") and is what the quote endpoint expects.
/// </summary>
public sealed record CryptoAsset(
    string Id,
    string Name,
    string Symbol,
    int? Rank,
    string? Type);
