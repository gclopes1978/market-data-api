using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MarketData.Api;

/// <summary>
/// Registers the API-wide rate limiter: a fixed window per client IP address that answers
/// <c>429 Too Many Requests</c> (RFC 9457 problem details plus <c>Retry-After</c>) once the budget is spent.
/// </summary>
public static class RateLimitingExtensions
{
    private const string HealthPath = "/health";

    /// <summary>
    /// Adds the rate limiter and binds <see cref="RateLimitingOptions"/>. The options are read when the
    /// limiter is first used, so configuration overrides (and test overrides) are honoured.
    /// Call <c>app.UseRateLimiter()</c> to activate it.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.Section))
            .Validate(
                o => o.PermitLimit > 0 && o.WindowSeconds > 0,
                "RateLimiting:PermitLimit and RateLimiting:WindowSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>(
                (limiter, settings) => Configure(limiter, settings.Value));

        return services;
    }

    private static void Configure(RateLimiterOptions limiter, RateLimitingOptions settings)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // Probes (container orchestrators, uptime monitors) must never be throttled.
            if (!settings.Enabled || context.Request.Path.StartsWithSegments(HealthPath))
                return RateLimitPartition.GetNoLimiter("unlimited");

            // Behind a reverse proxy this is the proxy's address unless forwarded headers are configured.
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });

        limiter.OnRejected = async (context, _) =>
        {
            var http = context.HttpContext;

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                http.Response.Headers[HeaderNames.RetryAfter] =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
            }

            http.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("MarketData.RateLimiting")
                .LogInformation("Rate limit exceeded for {Path}.", http.Request.Path);

            await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: "The request rate limit was exceeded. Retry after the interval given in the Retry-After header.")
                .ExecuteAsync(http);
        };
    }
}
