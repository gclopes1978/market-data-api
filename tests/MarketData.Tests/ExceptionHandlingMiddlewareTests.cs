using System.Net;
using System.Text.Json;
using System.Xml;
using MarketData.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace MarketData.Tests;

public sealed class ExceptionHandlingMiddlewareTests
{
    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private static DefaultHttpContext NewContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() }
    };

    private static async Task<(DefaultHttpContext Context, string Body)> RunAsync(
        RequestDelegate next, DefaultHttpContext? context = null)
    {
        context ??= NewContext();
        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return (context, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    public static TheoryData<Exception, int> Failures => new()
    {
        { new BrokenCircuitException("open"), 503 },
        { new TimeoutRejectedException(TimeSpan.FromSeconds(5)), 504 },
        { new TimeoutException("slow"), 504 },
        { new OperationCanceledException("http client timeout"), 504 },
        { new HttpRequestException("down"), 502 },
        { new JsonException("bad json"), 502 },
        { new FormatException("bad format"), 502 },
        { new XmlException("bad xml"), 502 },
        { new BadHttpRequestException("malformed", StatusCodes.Status400BadRequest), 400 },
        { new InvalidOperationException("boom"), 500 }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Maps_exception_to_problem_details_with_the_expected_status(Exception failure, int expected)
    {
        var (context, body) = await RunAsync(_ => throw failure);

        Assert.Equal(expected, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(expected, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Never_leaks_the_exception_message_to_the_caller()
    {
        var (context, body) = await RunAsync(_ => throw new InvalidOperationException("secret-connection-string"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("secret-connection-string", body);
    }

    [Fact]
    public async Task Open_circuit_sets_retry_after_from_the_breaker()
    {
        var (context, _) = await RunAsync(
            _ => throw new BrokenCircuitException("open", TimeSpan.FromSeconds(7)));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("7", context.Response.Headers["Retry-After"].ToString());
    }

    [Fact]
    public async Task Client_disconnect_yields_499_without_a_body()
    {
        var context = NewContext();
        context.RequestAborted = new CancellationToken(canceled: true);

        var (result, body) = await RunAsync(_ => throw new OperationCanceledException(), context);

        Assert.Equal(499, result.Response.StatusCode);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public async Task Successful_requests_pass_through_untouched()
    {
        var (context, body) = await RunAsync(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public async Task Rethrows_when_the_response_has_already_started()
    {
        var context = NewContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            RunAsync(_ => throw new HttpRequestException("late"), context));
    }

    [Fact]
    public async Task Open_circuit_from_a_provider_is_returned_as_503_by_the_running_api()
    {
        using var factory = new ApiFactory();
        factory.Fx.Failure = new BrokenCircuitException("open", TimeSpan.FromSeconds(30));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/fx/EUR/USD");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Unexpected_provider_exception_is_returned_as_generic_500_by_the_running_api()
    {
        using var factory = new ApiFactory();
        factory.Fx.Failure = new InvalidOperationException("internal detail");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/fx/EUR/USD");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("internal detail", body);
    }
}
