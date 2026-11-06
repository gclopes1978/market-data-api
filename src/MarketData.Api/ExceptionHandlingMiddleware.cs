using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace MarketData.Api;

/// <summary>
/// Last line of defence: turns any exception that escapes an endpoint into an RFC 9457 problem details
/// response with a stable status code, instead of a raw 500 or a leaked stack trace.
/// </summary>
/// <remarks>
/// Mapping: open circuit breaker → 503; timeouts → 504; upstream transport or
/// payload failures → 502 (titled after the route's <see cref="UpstreamMetadata"/> when it has one); malformed requests → their own 4xx; client disconnects → 499 (no body);
/// anything else → a generic 500. Exception messages are logged, never returned to the caller.
/// </remarks>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private const int ClientClosedRequest = 499;

    private sealed record Failure(
        int Status,
        string Title,
        string Detail,
        LogLevel Level);

    /// <summary>Runs the rest of the pipeline and converts any unhandled exception into a problem details response.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Too late to change the status code or body: let the server abort the connection.
                logger.LogError(ex, "Unhandled exception after the response had started.");
                throw;
            }

            if (ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                logger.LogDebug("Request cancelled by the client.");
                context.Response.StatusCode = ClientClosedRequest;
                return;
            }

            var failure = Describe(ex, context.GetEndpoint()?.Metadata.GetMetadata<UpstreamMetadata>()?.Name);
            logger.Log(failure.Level, ex, "Request failed with {StatusCode}: {Title}.", failure.Status, failure.Title);

            context.Response.Clear();

            await Results.Problem(
                statusCode: failure.Status,
                title: failure.Title,
                detail: failure.Detail,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier
                })
                .ExecuteAsync(context);
        }
    }

    // `asset` comes from the endpoint's UpstreamMetadata; without it the messages stay generic.
    private static Failure Describe(Exception ex, string? asset)
    {
        var subject = asset?.ToLowerInvariant();

        string Title(string generic, string named) => asset is null ? generic : named;

        return ex switch
        {
            BrokenCircuitException => new(
                StatusCodes.Status503ServiceUnavailable,
                Title("Data provider temporarily unavailable", $"{asset} provider temporarily unavailable"),
                "The upstream data provider is failing repeatedly, so the request was not attempted. Try again shortly.",
                LogLevel.Warning),

            // TimeoutRejectedException is Polly's attempt timeout; OperationCanceledException is HttpClient's own
            // timeout (a client disconnect was already handled above).
            TimeoutRejectedException or TimeoutException or OperationCanceledException => new(
                StatusCodes.Status504GatewayTimeout,
                Title("Data provider timed out", $"{asset} provider timed out"),
                asset is null
                    ? "The data provider did not respond in time."
                    : $"The data provider did not respond in time for the {subject} request.",
                LogLevel.Warning),

            HttpRequestException => new(
                StatusCodes.Status502BadGateway,
                Title("Data provider unavailable", $"{asset} provider unavailable"),
                asset is null
                    ? "The data provider could not complete the request."
                    : $"The data provider could not complete the {subject} request.",
                LogLevel.Warning),

            JsonException or FormatException or XmlException => new(
                StatusCodes.Status502BadGateway,
                Title("Invalid data provider response", $"Invalid {subject} provider response"),
                asset is null
                    ? "The data provider returned a response that could not be understood."
                    : $"The data provider returned an invalid {subject} response.",
                LogLevel.Warning),

            BadHttpRequestException bad => new(
                bad.StatusCode,
                "Bad request",
                "The request could not be processed.",
                LogLevel.Information),

            _ => new(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "The server failed to process the request.",
                LogLevel.Error)
        };
    }
}

/// <summary>Pipeline registration helper for <see cref="ExceptionHandlingMiddleware"/>.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="ExceptionHandlingMiddleware"/>. Register it first so it wraps every other middleware and endpoint.
    /// </summary>
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
