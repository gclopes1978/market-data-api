namespace MarketData.Api;

/// <summary>
/// Endpoint metadata naming the upstream data a route depends on (e.g. <c>Euribor</c>, <c>Exchange rate</c>).
/// <see cref="ExceptionHandlingMiddleware"/> uses it to keep error titles specific
/// (<c>Euribor provider unavailable</c>) without every endpoint carrying its own try/catch.
/// </summary>
/// <param name="Name">Human-readable asset name, used in problem details titles and details.</param>
public sealed record UpstreamMetadata(string Name);

/// <summary>Route builder helpers for <see cref="UpstreamMetadata"/>.</summary>
public static class UpstreamMetadataExtensions
{
    /// <summary>Declares which upstream data this route serves, so provider failures are reported by name.</summary>
    public static RouteHandlerBuilder WithUpstream(this RouteHandlerBuilder builder, string name) =>
        builder.WithMetadata(new UpstreamMetadata(name));
}
