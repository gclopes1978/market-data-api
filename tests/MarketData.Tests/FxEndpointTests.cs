using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketData.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarketData.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeFxProvider Fx { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFxRateProvider>();
            services.AddSingleton<IFxRateProvider>(Fx);
        });
}

public sealed class FxEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Returns_200_and_normalizes_codes_to_upper_case()
    {
        factory.Fx.Reset();
        factory.Fx.Result = new FxRate("EUR", "USD", 1.1193m, new DateOnly(2026, 10, 23), "Test");

        var resp = await _client.GetAsync("/api/fx/eur/usd");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("EUR", factory.Fx.LastBase);
        Assert.Equal("USD", factory.Fx.LastQuote);
    }

    [Fact]
    public async Task Returns_404_problem_details_when_provider_has_no_rate()
    {
        factory.Fx.Reset();

        var resp = await _client.GetAsync("/api/fx/EUR/XXX");
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Exchange rate not found", problem.Title);
    }

    [Theory]
    [InlineData("/api/fx/EU/USD")]
    [InlineData("/api/fx/EUR/US1")]
    [InlineData("/api/fx/EURO/USD")]
    public async Task Returns_400_problem_details_for_invalid_codes_without_calling_provider(string url)
    {
        factory.Fx.Reset();

        var resp = await _client.GetAsync(url);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid currency code", problem.Title);
        Assert.Equal(0, factory.Fx.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_instead_of_returning_504()
    {
        factory.Fx.Reset();

        var providerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        factory.Fx.Handler = async ct =>
        {
            providerStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        };

        using var cts = new CancellationTokenSource();
        var request = _client.GetAsync("/api/fx/EUR/USD", cts.Token);

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(1, factory.Fx.Calls);
    }
    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        var resp = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Returns_502_problem_details_when_provider_fails()
    {
        factory.Fx.Reset();
        factory.Fx.Failure = new HttpRequestException("Sensitive upstream details");

        var resp = await _client.GetAsync("/api/fx/EUR/USD");
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();

        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(502, problem.Status);
        Assert.Equal("Exchange rate provider unavailable", problem.Title);
        Assert.DoesNotContain("Sensitive upstream details", problem.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Returns_502_problem_details_when_provider_response_is_invalid()
    {
        factory.Fx.Reset();
        factory.Fx.Failure = new JsonException("Sensitive JSON details");

        var resp = await _client.GetAsync("/api/fx/EUR/USD");
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();

        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(502, problem.Status);
        Assert.Equal("Invalid exchange rate provider response", problem.Title);
        Assert.DoesNotContain("Sensitive JSON details", problem.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Returns_504_problem_details_when_provider_times_out()
    {
        factory.Fx.Reset();
        factory.Fx.Failure = new OperationCanceledException("Sensitive timeout details");

        var resp = await _client.GetAsync("/api/fx/EUR/USD");
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>();

        Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(504, problem.Status);
        Assert.Equal("Exchange rate provider timed out", problem.Title);
        Assert.DoesNotContain("Sensitive timeout details", problem.Detail ?? string.Empty);
    }

    private sealed record ProblemDetailsResponse(int Status, string Title, string Detail);
}
