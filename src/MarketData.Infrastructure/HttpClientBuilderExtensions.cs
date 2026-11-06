using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace MarketData.Infrastructure;

internal static class HttpClientBuilderExtensions
{
    // Polly's attempt timeout must fire just before HttpClient.Timeout: HttpClient's own timeout surfaces
    // as a plain cancellation, which a circuit breaker ignores, whereas a Polly timeout counts as a failure.
    private static readonly TimeSpan TimeoutMargin = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Puts a circuit breaker in front of an upstream HTTP client. Transport errors, timeouts, 5xx, 408 and 429
    /// responses count as failures; once the failure ratio is exceeded, calls fail fast with
    /// <see cref="Polly.CircuitBreaker.BrokenCircuitException"/> until the break duration elapses.
    /// </summary>
    /// <param name="builder">The typed or named HTTP client to protect.</param>
    /// <param name="name">Upstream name, used to name the pipeline (and so its telemetry).</param>
    /// <param name="settings">Breaker thresholds.</param>
    /// <param name="upstreamTimeout">The <see cref="HttpClient.Timeout"/> configured for this client.</param>
    internal static IHttpClientBuilder AddUpstreamCircuitBreaker(
        this IHttpClientBuilder builder,
        string name,
        CircuitBreakerSettings settings,
        TimeSpan upstreamTimeout)
    {
        if (!settings.Enabled)
            return builder;

        settings.Validate();

        builder.AddResilienceHandler($"{name}-circuit-breaker", pipeline =>
        {
            // Strategies run outside-in: the breaker observes what the timeout strategy throws.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = settings.FailureRatio,
                MinimumThroughput = settings.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(settings.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(settings.BreakDurationSeconds),
                ShouldHandle = args => args.Outcome switch
                {
                    { Exception: HttpRequestException or TimeoutRejectedException } => PredicateResult.True(),
                    { Result: { } response } when IsUpstreamFailure(response.StatusCode) => PredicateResult.True(),
                    _ => PredicateResult.False()
                }
            });

            pipeline.AddTimeout(upstreamTimeout - TimeoutMargin);
        });

        return builder;
    }

    private static bool IsUpstreamFailure(System.Net.HttpStatusCode status) =>
        (int)status >= 500 || status is System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests;
}
