namespace MarketData.Core;

public interface IStockSearchProvider
{
    Task<IReadOnlyList<StockSearchResult>> SearchAsync(
        string query,
        CancellationToken ct);
}