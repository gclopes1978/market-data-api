using MarketData.Core;

namespace MarketData.Infrastructure;

/// <summary>
/// Single entry point for bond quotes: picks the provider that owns the requested market.
/// The endpoint only knows IBondQuoteProvider, so adding a market means adding a case here
/// and a provider class, without touching the endpoint code.
/// </summary>
internal sealed class CompositeBondQuoteProvider(
    TreasuryBondQuoteProvider treasury,
    EcbBondQuoteProvider ecb) : IBondQuoteProvider
{
    public Task<MarketDataEnvelope<BondQuote>?> GetQuoteAsync(
        string market, string identifier, CancellationToken ct) =>
        market.ToUpperInvariant() switch
        {
            "US" => treasury.GetQuoteAsync(market, identifier, ct),
            "EUR" => ecb.GetQuoteAsync(market, identifier, ct),
            // Markets without a provider (e.g. GLOBAL) are rejected earlier by the endpoint.
            _ => Task.FromResult<MarketDataEnvelope<BondQuote>?>(null)
        };
}
