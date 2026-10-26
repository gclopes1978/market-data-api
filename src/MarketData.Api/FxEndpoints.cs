using System.Net;
using System.Text.Json;
using MarketData.Core;

namespace MarketData.Api;

public static class FxEndpoints
{
    public static IEndpointRouteBuilder MapFx(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fx");
        group.MapGet("/{baseCcy}/{quoteCcy}", GetRate);
        return app;
    }

    private static async Task<IResult> GetRate(
        string baseCcy, string quoteCcy, IFxRateProvider fx, CancellationToken ct)
    {
        // Validate input before it reaches the upstream URL
        static bool IsCode(string s) => s.Length == 3 && s.All(char.IsAsciiLetter);
        if (!IsCode(baseCcy) || !IsCode(quoteCcy))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid currency code",
                detail: "Use 3-letter currency codes.");
        }

        FxRate? rate;

        try
        {
            rate = await fx.GetRateAsync(
                baseCcy.ToUpperInvariant(),
                quoteCcy.ToUpperInvariant(),
                ct);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Exchange rate provider unavailable",
                detail: "The exchange rate provider could not complete the request.");
        }
        catch (JsonException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Invalid exchange rate provider response",
                detail: "The exchange rate provider returned an invalid response.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Exchange rate provider timed out",
                detail: "The exchange rate provider did not respond in time.");
        }

        return rate is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Exchange rate not found",
                detail: "No exchange rate was found for the requested currency pair.")
            : Results.Ok(rate);
    }
}
