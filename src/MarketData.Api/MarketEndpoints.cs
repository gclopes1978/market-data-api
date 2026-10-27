
using MarketData.Core;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using System.Xml;

namespace MarketData.Api;

public static class MarketEndpoints
{
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
    private static readonly string[] sourceArray = ["US2Y", "US5Y", "US10Y", "US30Y"];

    public static IEndpointRouteBuilder MapMarketData(
        this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/euribor/{tenor}", GetEuribor);
        api.MapGet("/bonds/{market}/{identifier}", GetBond);
        api.MapGet("/stocks/{ticker}", GetStock);
        api.MapGet("/etfs/{ticker}", GetEtf);
        api.MapGet("/commodities/{symbol}", GetCommodity);

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

        try
        {
            var result = await provider.GetRateAsync(
                tenor.ToUpperInvariant(), ct);

            return result is null
                ? NotFound("Euribor rate not found",
                    "No observation was returned for the requested tenor.")
                : Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return ProviderError("Euribor");
        }
        catch (FormatException)
        {
            return InvalidProviderResponse("Euribor");
        }
        catch (JsonException)
        {
            return InvalidProviderResponse("Euribor");
        }
        catch (XmlException)
        {
            return InvalidProviderResponse("Euribor");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProviderTimeout("Euribor");
        }
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

        if (market.Equals("US", StringComparison.OrdinalIgnoreCase) && identifier.Length == 4 &&
            identifier.StartsWith("US", StringComparison.OrdinalIgnoreCase) &&
            identifier.EndsWith("Y", StringComparison.OrdinalIgnoreCase) && 
            !sourceArray.Contains(identifier, StringComparer.OrdinalIgnoreCase))
        {
            return InvalidInput(
                "Invalid bond identifier",
                "Supported US Treasury maturities are US2Y, US5Y, US10Y and US30Y.");
        }
        if (!market.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderUnavailable("Bond");
        }

        try
        {
            var result = await provider.GetQuoteAsync(
                market.ToUpperInvariant(),
                identifier.ToUpperInvariant(),
                ct);

            return result is null
                ? NotFound("Bond quote not found",
                    "No quote was found for the requested bond.")
                : Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return ProviderError("Bond");
        }
        catch (JsonException)
        {
            return InvalidProviderResponse("Bond");
        }
        catch (XmlException)
        {
            return InvalidProviderResponse("Bond");
        }
        catch (FormatException)
        {
            return InvalidProviderResponse("Bond");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProviderTimeout("Bond");
        }
    }

    private static Task<IResult> GetStock(
        string ticker,
        [FromServices] IEquityQuoteProvider provider,
        CancellationToken ct) =>
        GetEquity(ticker, provider, ct, "Stock");

    private static Task<IResult> GetEtf(
        string ticker,
        [FromServices] IEquityQuoteProvider provider,
        CancellationToken ct) =>
        GetEquity(ticker, provider, ct, "ETF");

    private static async Task<IResult> GetEquity(
        string ticker,
        IEquityQuoteProvider provider,
        CancellationToken ct,
        string asset)
    {
        if (!IsIdentifier(ticker, 20))
        {
            return InvalidInput(
                $"Invalid {asset.ToLowerInvariant()} ticker",
                "Use 1 to 20 ASCII letters, digits, dots, hyphens or carets.");
        }

        try
        {
            var result = await provider.GetQuoteAsync(
                ticker.ToUpperInvariant(), ct);

            return result is null
                ? NotFound($"{asset} quote not found",
                    $"No quote was found for the requested {asset.ToLowerInvariant()}.")
                : Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return ProviderError(asset);
        }
        catch (JsonException)
        {
            return InvalidProviderResponse(asset);
        }
        catch (FormatException)
        {
            return InvalidProviderResponse(asset);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProviderTimeout(asset);
        }
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

        try
        {
            var result = await provider.GetQuoteAsync(
                symbol.ToUpperInvariant(), ct);

            return result is null
                ? NotFound("Commodity quote not found",
                    "No quote was found for the requested commodity.")
                : Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return ProviderError("Commodity");
        }
        catch (JsonException)
        {
            return InvalidProviderResponse("Commodity");
        }
        catch (FormatException)
        {
            return InvalidProviderResponse("Commodity");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProviderTimeout("Commodity");
        }
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

    private static IResult ProviderError(string asset) =>
        Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: $"{asset} provider unavailable",
            detail: $"The data provider could not complete the {asset.ToLowerInvariant()} request.");

    private static IResult InvalidProviderResponse(string asset) =>
        Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: $"Invalid {asset.ToLowerInvariant()} provider response",
            detail: $"The data provider returned an invalid {asset.ToLowerInvariant()} response.");

    private static IResult ProviderTimeout(string asset) =>
        Results.Problem(
            statusCode: StatusCodes.Status504GatewayTimeout,
            title: $"{asset} provider timed out",
            detail: $"The data provider did not respond in time for the {asset.ToLowerInvariant()} request.");
}
