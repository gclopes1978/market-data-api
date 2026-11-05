namespace MarketData.Core;

public interface IStockSearchProvider
{
    /// <summary>Searches stocks by symbol or company name. Returns an empty list when nothing matches.</summary>
    Task<IReadOnlyList<StockSearchResult>> SearchAsync(
        string query,
        CancellationToken ct);
}