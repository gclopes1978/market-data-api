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
            return Results.BadRequest("Use 3-letter currency codes.");

        var rate = await fx.GetRateAsync(baseCcy.ToUpperInvariant(), quoteCcy.ToUpperInvariant(), ct);
        return rate is null ? Results.NotFound() : Results.Ok(rate);
    }
}