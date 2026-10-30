using MarketData.Core;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MarketData.Api;

public static partial class MarketEndpoints
{
    private const int MaxCryptoSearchLength = 100;
    private const int MaxCryptoLimit = 1000;
    private const int MaxCryptoIdLength = 100;
    private const string DefaultCryptoCurrency = "USD";

    // Fiat currencies offered by the quote endpoint.
    private static readonly HashSet<string> CryptoCurrencies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "USD", "EUR"
        };

    /// <summary>
    /// GET /api/crypto — every active cryptocurrency, ordered by market-cap rank.
    /// Optional <c>q</c> filters by name or symbol; optional <c>limit</c> caps the result.
    /// </summary>
    private static async Task<IResult> ListCrypto(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromServices] ICryptoCatalogProvider provider,
        CancellationToken ct)
    {
        var term = q?.Trim();

        if (term is { Length: > MaxCryptoSearchLength })
        {
            return InvalidInput(
                "Invalid crypto search query",
                $"The search query must not exceed {MaxCryptoSearchLength} characters.");
        }

        if (limit is < 1 or > MaxCryptoLimit)
        {
            return InvalidInput(
                "Invalid crypto limit",
                $"The limit must be between 1 and {MaxCryptoLimit}.");
        }

        try
        {
            var catalog = await provider.GetAssetsAsync(ct);
            IEnumerable<CryptoAsset> assets = catalog.Data;

            if (!string.IsNullOrEmpty(term))
            {
                // OrderBy is stable, so after the exact-symbol matches the rank order is kept.
                assets = assets
                    .Where(a =>
                        a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        a.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(a =>
                        a.Symbol.Equals(term, StringComparison.OrdinalIgnoreCase));
            }

            if (limit is { } max)
                assets = assets.Take(max);

            return Results.Ok(
                new MarketDataEnvelope<IReadOnlyList<CryptoAsset>>(
                    assets.ToList(), catalog.Metadata));
        }
        catch (HttpRequestException)
        {
            return ProviderError("Crypto");
        }
        catch (JsonException)
        {
            return InvalidProviderResponse("Crypto");
        }
        catch (FormatException)
        {
            return InvalidProviderResponse("Crypto");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProviderTimeout("Crypto");
        }
    }

    /// <summary>
    /// GET /api/crypto/{id} — latest quote of one cryptocurrency (id as listed by /api/crypto,
    /// e.g. "btc-bitcoin"). Optional <c>currency</c>: USD (default) or EUR.
    /// </summary>
    private static async Task<IResult> GetCrypto(
        string id,
        [FromQuery] string? currency,
        [FromServices] ICryptoQuoteProvider provider,
        CancellationToken ct)
    {
        if (!IsCryptoId(id))
        {
            return InvalidInput(
                "Invalid crypto id",
                $"Use 1 to {MaxCryptoIdLength} ASCII letters, digits, dots, hyphens or underscores.");
        }

        var quoteCurrency = string.IsNullOrWhiteSpace(currency)
            ? DefaultCryptoCurrency
            : currency.Trim().ToUpperInvariant();

        if (!CryptoCurrencies.Contains(quoteCurrency))
        {
            return InvalidInput(
                "Invalid crypto currency",
                "Supported currencies are " +
                string.Join(", ", CryptoCurrencies.Order()) + ".");
        }

        try
        {
            var result = await provider.GetQuoteAsync(
                id.ToLowerInvariant(), quoteCurrency, ct);

            return result is null
                ? NotFound("Crypto quote not found",
                    "No quote was found for the requested cryptocurrency.")
                : Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return ProviderError("Crypto");
        }
        catch (JsonException)
        {
            return InvalidProviderResponse("Crypto");
        }
        catch (FormatException)
        {
            return InvalidProviderResponse("Crypto");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProviderTimeout("Crypto");
        }
    }

    private static bool IsCryptoId(string value) =>
        value.Length >= 1 &&
        value.Length <= MaxCryptoIdLength &&
        value.All(c =>
            char.IsAsciiLetterOrDigit(c) ||
            c is '.' or '-' or '_');
}
