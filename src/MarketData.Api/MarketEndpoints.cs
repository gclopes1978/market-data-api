
using MarketData.Core;
using Microsoft.AspNetCore.Mvc;

namespace MarketData.Api;

public static partial class MarketEndpoints
{
    private const string Euribor= "Euribor";
    private const string Bond = "Bond";

    private const string SearchStock = "Stock search";

    private const string Commodity = "Commodity";
    private static readonly HashSet<string> EuriborTenors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "1M", "3M", "6M", "12M"
        };

    private static readonly HashSet<string> BondMarkets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "EUR", "US", "GLOBAL"
        };

    // Maturities each bond market accepts. They mirror what the providers can actually serve:
    // US = U.S. Treasury par yield curve, EUR = ECB euro area AAA yield curve.
    private static readonly HashSet<string> UsMaturities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "US1M", "US3M", "US6M", "US1Y", "US2Y",
            "US5Y", "US7Y", "US10Y", "US20Y", "US30Y"
        };

    private static readonly HashSet<string> EurMaturities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "EUR3M", "EUR6M", "EUR1Y", "EUR2Y",
            "EUR5Y", "EUR7Y", "EUR10Y", "EUR20Y", "EUR30Y"
        };

    /// <summary>Maps the <c>/api</c> market data endpoints: Euribor, bonds, stocks (search and quote), ETFs, commodities and crypto.</summary>
    public static IEndpointRouteBuilder MapMarketData(
        this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // WithUpstream names the data each route depends on; ExceptionHandlingMiddleware turns provider
        // failures (502/503/504) into problem details titled after it.
        api.MapGet("/euribor/{tenor}", GetEuribor).WithUpstream(Euribor);
        api.MapGet("/bonds/{market}/{identifier}", GetBond).WithUpstream(Bond);
        api.MapGet("/stocks/search", SearchStocks).WithUpstream(SearchStock);
        api.MapGet("/stocks/{ticker}", GetStock).WithUpstream("Stock");
        api.MapGet("/etfs/{ticker}", GetEtf).WithUpstream("ETF");
        api.MapGet("/commodities/{symbol}", GetCommodity).WithUpstream(Commodity);
        api.MapGet("/crypto", ListCrypto).WithUpstream(Crypto);
        api.MapGet("/crypto/{id}", GetCrypto).WithUpstream(Crypto);

        return app;
    }

    private static async Task<IResult> GetEuribor(
        string tenor,
        [FromServices] IEuriborRateProvider provider,
        CancellationToken ct)
    {
        if (!EuriborTenors.Contains(tenor))
        {
            return InvalidInput(
                "Invalid Euribor tenor",
                "Supported tenors are 1M, 3M, 6M and 12M.");
        }

        var result = await provider.GetRateAsync(
            tenor.ToUpperInvariant(), ct);

        return result is null
            ? NotFound("Euribor rate not found",
                "No observation was returned for the requested tenor.")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetBond(
        string market,
        string identifier,
        [FromServices] IBondQuoteProvider provider,
        CancellationToken ct)
    {
        if (!BondMarkets.Contains(market))
        {
            return InvalidInput(
                "Invalid bond market",
                "Supported markets are EUR, US and GLOBAL.");
        }

        if (!IsIdentifier(identifier, 32))
        {
            return InvalidInput(
                "Invalid bond identifier",
                "Use 1 to 32 ASCII letters, digits, dots, hyphens or carets.");
        }

        // GLOBAL is accepted as a market name but has no data provider yet.
        if (market.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderUnavailable(Bond);
        }

        // Validate the maturity against the list of the chosen market (400 instead of a misleading 404).
        var isUs = market.Equals("US", StringComparison.OrdinalIgnoreCase);
        if (!(isUs ? UsMaturities : EurMaturities).Contains(identifier))
        {
            return InvalidInput(
                "Invalid bond identifier",
                isUs
                    ? "Supported US Treasury maturities are " + string.Join(", ", UsMaturities.Order()) + "."
                    : "Supported euro area maturities are " + string.Join(", ", EurMaturities.Order()) + ".");
        }

        var result = await provider.GetQuoteAsync(
            market.ToUpperInvariant(),
            identifier.ToUpperInvariant(),
            ct);

        return result is null
            ? NotFound("Bond quote not found",
                "No quote was found for the requested bond.")
            : Results.Ok(result);
    }

    private static async Task<IResult> SearchStocks([FromQuery] string? q, [FromServices] IStockSearchProvider provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return InvalidInput(
                "Invalid stock search query",
                "Provide at least 2 characters to search for stocks.");
        }

        if (q.Trim().Length > 100)
        {
            return InvalidInput(
                "Invalid stock search query",
                "The search query must not exceed 100 characters.");
        }

        var results = await provider.SearchAsync(q.Trim(), ct);
        return Results.Ok(results);
    }

    private static Task<IResult> GetStock(
        string ticker,
        [FromServices] IEquityQuoteProvider provider,
        CancellationToken ct) =>
        GetEquity(ticker, provider,  "Stock", ct);

    private static Task<IResult> GetEtf(
        string ticker,
        [FromServices] IEquityQuoteProvider provider,
        CancellationToken ct) =>
        GetEquity(ticker, provider,  "ETF", ct);

    private static async Task<IResult> GetEquity(
        string ticker,
        IEquityQuoteProvider provider,
        string asset,
        CancellationToken ct)
    {
        if (!IsIdentifier(ticker, 20))
        {
            return InvalidInput(
                $"Invalid {asset.ToLowerInvariant()} ticker",
                "Use 1 to 20 ASCII letters, digits, dots, hyphens or carets.");
        }

        var result = await provider.GetQuoteAsync(
            ticker.ToUpperInvariant(), ct);

        return result is null
            ? NotFound($"{asset} quote not found",
                $"No quote was found for the requested {asset.ToLowerInvariant()}.")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetCommodity(
        string symbol,
        [FromServices] ICommodityQuoteProvider provider,
        CancellationToken ct)
    {
        if (!IsIdentifier(symbol, 20))
        {
            return InvalidInput(
                "Invalid commodity symbol",
                "Use 1 to 20 ASCII letters, digits, dots, hyphens or carets.");
        }

        var result = await provider.GetQuoteAsync(
            symbol.ToUpperInvariant(), ct);

        return result is null
            ? NotFound("Commodity quote not found",
                "No quote was found for the requested commodity.")
            : Results.Ok(result);
    }

    private static bool IsIdentifier(string value, int maxLength) =>
        value.Length >= 1 &&
        value.Length <= maxLength &&
        value.All(c =>
            char.IsAsciiLetterOrDigit(c) ||
            c is '.' or '-' or '^');

    private static IResult InvalidInput(string title, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: title,
            detail: detail);

    private static IResult NotFound(string title, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: title,
            detail: detail);

    private static IResult ProviderUnavailable(string asset) =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: $"{asset} data provider not configured",
            detail: $"No data provider is currently configured for {asset}.");
}
