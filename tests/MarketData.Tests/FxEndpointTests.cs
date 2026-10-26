using System.Net;
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
    public async Task Returns_404_when_provider_has_no_rate()
    {
        factory.Fx.Reset();

        var resp = await _client.GetAsync("/api/fx/EUR/XXX");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Theory]
    [InlineData("/api/fx/EU/USD")]
    [InlineData("/api/fx/EUR/US1")]
    [InlineData("/api/fx/EURO/USD")]
    public async Task Returns_400_for_invalid_codes_without_calling_provider(string url)
    {
        factory.Fx.Reset();

        var resp = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(0, factory.Fx.Calls);
    }

    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        var resp = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}