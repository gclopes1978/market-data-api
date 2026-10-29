namespace MarketData.Core;

public sealed record MarketDataMetadata(string Source, DateOnly AsOf);

public sealed record MarketDataEnvelope<T>(
    T Data,
    MarketDataMetadata Metadata);

public sealed record EuriborRate(
    string Tenor,
    decimal Rate,
    string Unit,
    string Frequency);

public sealed record BondQuote(
    string Identifier,
    string Market,
    decimal? Price,
    string Currency,
    decimal? Yield,
    DateOnly? MaturityDate);

public sealed record EquityQuote(
    string Symbol,
    string? Name,
    decimal Price,
    string Currency,
    decimal? ChangePercent,
    long? Volume);

public sealed record CommodityQuote(
    string Symbol,
    string Name,
    decimal Price,
    string Currency,
    string Unit);

public sealed record CryptoQuote(
    string Id,
    string Symbol,
    string Name,
    decimal Price,
    string Currency,
    decimal? ChangePercent24h,
    decimal? Volume24h,
    decimal? MarketCap,
    int? Rank);
