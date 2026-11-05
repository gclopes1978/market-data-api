namespace MarketData.Api;

/// <summary>
/// Settings of the per-client rate limiter, bound from the <c>RateLimiting</c> configuration section.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Name of the configuration section these options are bound from.</summary>
    public const string Section = "RateLimiting";

    /// <summary>Turns the limiter off entirely (e.g. for local load tests). Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of requests a single client may make per window. Defaults to 60.</summary>
    public int PermitLimit { get; set; } = 60;

    /// <summary>Length of the fixed window, in seconds. Defaults to 60.</summary>
    public int WindowSeconds { get; set; } = 60;
}
