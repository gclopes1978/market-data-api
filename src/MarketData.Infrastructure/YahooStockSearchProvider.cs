using MarketData.Core;

namespace MarketData.Infrastructure;

internal sealed class YahooStockSearchProvider(
    YahooFinanceClient yahoo) : IStockSearchProvider
{
    public Task<IReadOnlyList<StockSearchResult>> SearchAsync(
        string query,
        CancellationToken ct) =>
        yahoo.SearchStocksAsync(query, ct);
}