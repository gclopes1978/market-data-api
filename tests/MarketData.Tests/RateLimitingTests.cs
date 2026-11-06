using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketData.Api;
using MarketData.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarketData.Tests;

public sealed class RateLimitingTests
{
    private static WebApplicationFactory<Program> CreateFactory(Action<RateLimitingOptions> configure)
    {
        var fx = new FakeFxProvider
        {
            Result = new FxRate("EUR", "USD", 1.1m, new DateOnly(2026, 10, 23), "Test")
        };

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFxRateProvider>();
                services.AddSingleton<IFxRateProvider>(fx);
                services.Configure(configure);
            }));
    }

    [Fact]
    public async Task Returns_429_problem_details_with_retry_after_once_the_limit_is_exceeded()
    {
        await using var factory = CreateFactory(o => { o.PermitLimit = 2; o.WindowSeconds = 60; });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/fx/EUR/USD")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/fx/EUR/USD")).StatusCode);

        using var limited = await client.GetAsync("/api/fx/EUR/USD");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.True(limited.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.Parse(values!.Single()) >= 1);

        using var body = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Health_endpoint_is_never_rate_limited()
    {
        await using var factory = CreateFactory(o => { o.PermitLimit = 1; o.WindowSeconds = 60; });
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Requests_are_not_limited_when_the_limiter_is_disabled()
    {
        await using var factory = CreateFactory(o => { o.Enabled = false; o.PermitLimit = 1; });
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/fx/EUR/USD")).StatusCode);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(10, 0)]
    public void Startup_fails_when_the_limiter_settings_are_invalid(int permitLimit, int windowSeconds)
    {
        using var factory = CreateFactory(o => { o.PermitLimit = permitLimit; o.WindowSeconds = windowSeconds; });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
