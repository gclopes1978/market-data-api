using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MarketData.Core;
using Microsoft.Extensions.Caching.Memory;

namespace MarketData.Infrastructure;

/// <summary>
/// Crypto catalog and quotes from the CoinPaprika public API (free tier, no API key).
/// The catalog is large and rarely changes, so it is cached for an hour; quotes are cached
/// briefly to stay well inside the free monthly quota.
/// </summary>
public sealed class CoinPaprikaCryptoProvider(
    HttpClient http,
    IMemoryCache cache) : ICryptoCatalogProvider, ICryptoQuoteProvider
{
    public const string SourceName = "CoinPaprika";

    private const string CatalogCacheKey = "crypto:catalog";

    public static readonly TimeSpan CatalogTtl = TimeSpan.FromHours(1);
    public static readonly TimeSpan QuoteTtl = TimeSpan.FromSeconds(30);

    private sealed record CoinDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("rank")] int? Rank,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("type")] string? Type);

    private sealed record QuoteDto(
        [property: JsonPropertyName("price")] decimal? Price,
        [property: JsonPropertyName("volume_24h")] decimal? Volume24h,
        [property: JsonPropertyName("market_cap")] decimal? MarketCap,
        [property: JsonPropertyName("percent_change_24h")] decimal? PercentChange24h);

    private sealed record TickerDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("rank")] int? Rank,
        [property: JsonPropertyName("last_updated")] DateTimeOffset? LastUpdated,
        [property: JsonPropertyName("quotes")] Dictionary<string, QuoteDto>? Quotes);

    public async Task<MarketDataEnvelope<IReadOnlyList<CryptoAsset>>> GetAssetsAsync(
        CancellationToken ct)
    {
        if (cache.TryGetValue(CatalogCacheKey,
                out MarketDataEnvelope<IReadOnlyList<CryptoAsset>>? cached)
            && cached is not null)
        {
            return cached;
        }

        using var resp = await http.GetAsync("coins", ct);
        resp.EnsureSuccessStatusCode();

        var coins = await resp.Content.ReadFromJsonAsync<List<CoinDto>>(
                        cancellationToken: ct)
                    ?? throw new FormatException(
                        "CoinPaprika returned an empty coin list.");

        // Inactive coins are dead projects; rank 0 means "unranked" and is sent to the end.
        IReadOnlyList<CryptoAsset> assets = coins
            .Where(c => c.IsActive
                        && !string.IsNullOrWhiteSpace(c.Id)
                        && !string.IsNullOrWhiteSpace(c.Name)
                        && !string.IsNullOrWhiteSpace(c.Symbol))
            .Select(c => new CryptoAsset(
                c.Id!, c.Name!, c.Symbol!, c.Rank > 0 ? c.Rank : null, c.Type))
            .OrderBy(a => a.Rank ?? int.MaxValue)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var envelope = new MarketDataEnvelope<IReadOnlyList<CryptoAsset>>(
            assets,
            new MarketDataMetadata(
                SourceName, DateOnly.FromDateTime(DateTime.UtcNow)));

        cache.Set(CatalogCacheKey, envelope, CatalogTtl);
        return envelope;
    }

    public async Task<MarketDataEnvelope<CryptoQuote>?> GetQuoteAsync(
        string id,
        string currency,
        CancellationToken ct)
    {
        var coinId = id.ToLowerInvariant();
        var quoteCurrency = currency.ToUpperInvariant();
        var cacheKey = $"crypto:quote:{coinId}:{quoteCurrency}";

        if (cache.TryGetValue(cacheKey,
                out MarketDataEnvelope<CryptoQuote>? cached)
            && cached is not null)
        {
            return cached;
        }

        var path = $"tickers/{Uri.EscapeDataString(coinId)}" +
                   $"?quotes={Uri.EscapeDataString(quoteCurrency)}";

        using var resp = await http.GetAsync(path, ct);

        // Unknown coin id: treat as not found.
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        resp.EnsureSuccessStatusCode();

        var ticker = await resp.Content.ReadFromJsonAsync<TickerDto>(
            cancellationToken: ct);

        if (ticker?.Quotes is null
            || !ticker.Quotes.TryGetValue(quoteCurrency, out var quote)
            || quote.Price is null
            || string.IsNullOrWhiteSpace(ticker.Id)
            || string.IsNullOrWhiteSpace(ticker.Name)
            || string.IsNullOrWhiteSpace(ticker.Symbol))
        {
            return null;
        }

        var asOf = DateOnly.FromDateTime(
            ticker.LastUpdated?.UtcDateTime ?? DateTime.UtcNow);

        var envelope = new MarketDataEnvelope<CryptoQuote>(
            new CryptoQuote(
                ticker.Id,
                ticker.Symbol,
                ticker.Name,
                quote.Price.Value,
                quoteCurrency,
                quote.PercentChange24h,
                quote.Volume24h,
                quote.MarketCap,
                ticker.Rank > 0 ? ticker.Rank : null),
            new MarketDataMetadata(SourceName, asOf));

        cache.Set(cacheKey, envelope, QuoteTtl);
        return envelope;
    }
}
