using MarketData.Core;
using Microsoft.AspNetCore.Mvc;

namespace MarketData.Api;

public static class FxEndpoints
{
    /// <summary>Maps <c>GET /api/fx/{baseCcy}/{quoteCcy}</c>.</summary>
    public static IEndpointRouteBuilder MapFx(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fx");
        group.MapGet("/{baseCcy}/{quoteCcy}", GetRate).WithUpstream("Exchange rate");
        return app;
    }

    private static async Task<IResult> GetRate(string baseCcy, string quoteCcy, [FromServices] IFxRateProvider fx, CancellationToken ct)
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

        var rate = await fx.GetRateAsync(
            baseCcy.ToUpperInvariant(),
            quoteCcy.ToUpperInvariant(),
            ct);

        return rate is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Exchange rate not found",
                detail: "No exchange rate was found for the requested currency pair.")
            : Results.Ok(rate);
    }
}
