namespace MarketData.Core;

public sealed record StockSearchResult(
    string Symbol,
    string Name,
    string? Exchange,
    string? ExchangeName);