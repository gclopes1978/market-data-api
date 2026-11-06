using MarketData.Core;

namespace MarketData.Infrastructure;

internal sealed class YahooStockSearchProvider(
    YahooFinanceClient yahoo) : IStockSearchProvider
{
    /// <summary>Searches stocks through the Yahoo Finance search endpoint.</summary>
    public Task<IReadOnlyList<StockSearchResult>> SearchAsync(
        string query,
        CancellationToken ct) =>
        yahoo.SearchStocksAsync(query, ct);
}