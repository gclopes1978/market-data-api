using System.Net;
using System.Text.Json;
using MarketData.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarketData.Tests;

public sealed class MarketEndpointsTests(
    WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EuriborSource =
        "ECB Data Portal (financial market data)";

    private static readonly DateOnly AsOf = new(2026, 9, 1);

    private HttpClient CreateTestClient(
        Func<string, CancellationToken,
            Task<MarketDataEnvelope<EuriborRate>?>>? euriborResponse = null,
        IEquityQuoteProvider? equityProvider = null,
        ICommodityQuoteProvider? commodityProvider = null,
        IBondQuoteProvider? bondProvider = null)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEuriborRateProvider>();
                services.AddSingleton<IEuriborRateProvider>(
                    new FakeEuriborRateProvider(
                        euriborResponse ?? ValidEuriborResponse));

                services.RemoveAll<IEquityQuoteProvider>();
                services.AddSingleton<IEquityQuoteProvider>(
                    equityProvider ?? new FakeEquityQuoteProvider());

                services.RemoveAll<ICommodityQuoteProvider>();
                services.AddSingleton<ICommodityQuoteProvider>(
                    commodityProvider ?? new FakeCommodityQuoteProvider());

                services.RemoveAll<IBondQuoteProvider>();
                services.AddSingleton<IBondQuoteProvider>(
                    bondProvider ?? new FakeBondQuoteProvider());
            })).CreateClient();
    }

    private static Task<MarketDataEnvelope<EuriborRate>?> ValidEuriborResponse(
        string tenor, CancellationToken ct) =>
        Task.FromResult<MarketDataEnvelope<EuriborRate>?>(
            new MarketDataEnvelope<EuriborRate>(
                new EuriborRate(tenor, 2.125m,
                    "Percent per annum", "Monthly average"),
                new MarketDataMetadata(EuriborSource, AsOf)));

    [Theory]
    [InlineData("1M")]
    [InlineData("3M")]
    [InlineData("6M")]
    [InlineData("12M")]
    public async Task ValidEuriborRequest_ReturnsRateAndMetadata(string tenor)
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync($"/api/euribor/{tenor}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var data = root.GetProperty("data");
        var metadata = root.GetProperty("metadata");

        Assert.Equal(tenor, data.GetProperty("tenor").GetString());
        Assert.Equal(2.125m, data.GetProperty("rate").GetDecimal());
        Assert.Equal("Percent per annum", data.GetProperty("unit").GetString());
        Assert.Equal("Monthly average", data.GetProperty("frequency").GetString());
        Assert.Equal(EuriborSource, metadata.GetProperty("source").GetString());
        Assert.Equal("2026-09-01", metadata.GetProperty("asOf").GetString());
    }

    [Fact]
    public async Task EuriborRequest_WhenNoObservation_Returns404()
    {
        using var client = CreateTestClient(
            (_, _) => Task.FromResult<MarketDataEnvelope<EuriborRate>?>(null));

        using var response = await client.GetAsync("/api/euribor/3M");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EuriborRequest_WhenProviderFails_Returns502()
    {
        using var client = CreateTestClient(
            (_, _) => Task.FromException<MarketDataEnvelope<EuriborRate>?>(
                new HttpRequestException("Simulated provider failure")));

        using var response = await client.GetAsync("/api/euribor/1M");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task EuriborRequest_WhenCsvIsInvalid_Returns502()
    {
        using var client = CreateTestClient(
            (_, _) => Task.FromException<MarketDataEnvelope<EuriborRate>?>(
                new FormatException("Simulated invalid CSV")));

        using var response = await client.GetAsync("/api/euribor/6M");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task EuriborRequest_WhenProviderTimesOut_Returns504()
    {
        using var client = CreateTestClient(
            (_, _) => Task.FromException<MarketDataEnvelope<EuriborRate>?>(
                new OperationCanceledException("Simulated timeout")));

        using var response = await client.GetAsync("/api/euribor/12M");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/stocks/AAPL")]
    [InlineData("/api/etfs/SPY")]
    public async Task EquityRequests_ReturnQuoteAndMetadata(string path)
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(123.45m,
            document.RootElement.GetProperty("data")
                .GetProperty("price").GetDecimal());
        Assert.Equal("Test provider",
            document.RootElement.GetProperty("metadata")
                .GetProperty("source").GetString());
    }

    [Fact]
    public async Task CommodityRequest_ReturnsQuoteAndUnit()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/commodities/GOLD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var data = document.RootElement.GetProperty("data");
        Assert.Equal("GOLD", data.GetProperty("symbol").GetString());
        Assert.Equal("USD per troy ounce", data.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task UsBondRequest_ReturnsYieldWithoutInventedPrice()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/bonds/US/US10Y");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var data = document.RootElement.GetProperty("data");
        Assert.Equal(3.75m, data.GetProperty("yield").GetDecimal());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("price").ValueKind);
    }

    [Fact]
    public async Task NonUsBondMarket_Returns503()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/bonds/EUR/US10Y");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UnknownCommodity_Returns404()
    {
        using var client = CreateTestClient(
            commodityProvider: new FakeCommodityQuoteProvider(returnNull: true));

        using var response = await client.GetAsync("/api/commodities/UNKNOWN");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/euribor/2M")]
    [InlineData("/api/bonds/INVALID/US10Y")]
    [InlineData("/api/bonds/EUR/bad_symbol!")]
    [InlineData("/api/bonds/US/US4Y")]
    [InlineData("/api/stocks/MSFT!")]
    [InlineData("/api/etfs/SPY!")]
    [InlineData("/api/commodities/XAU!")]
    public async Task InvalidRequest_Returns400(string path)
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(400,
            document.RootElement.GetProperty("status").GetInt32());
    }

    private sealed class FakeEuriborRateProvider(
        Func<string, CancellationToken,
            Task<MarketDataEnvelope<EuriborRate>?>> response)
        : IEuriborRateProvider
    {
        public Task<MarketDataEnvelope<EuriborRate>?> GetRateAsync(
            string tenor, CancellationToken ct) => response(tenor, ct);
    }

    private sealed class FakeEquityQuoteProvider : IEquityQuoteProvider
    {
        public Task<MarketDataEnvelope<EquityQuote>?> GetQuoteAsync(
            string ticker, CancellationToken ct) =>
            Task.FromResult<MarketDataEnvelope<EquityQuote>?>(
                new MarketDataEnvelope<EquityQuote>(
                    new EquityQuote(ticker, "Test asset", 123.45m,
                        "USD", 1.2m, 1000),
                    new MarketDataMetadata("Test provider", AsOf)));
    }

    private sealed class FakeCommodityQuoteProvider(
        bool returnNull = false) : ICommodityQuoteProvider
    {
        public Task<MarketDataEnvelope<CommodityQuote>?> GetQuoteAsync(
            string symbol, CancellationToken ct) =>
            Task.FromResult<MarketDataEnvelope<CommodityQuote>?>(
                returnNull
                    ? null
                    : new MarketDataEnvelope<CommodityQuote>(
                        new CommodityQuote(symbol, "Test commodity",
                            2500m, "USD", "USD per troy ounce"),
                        new MarketDataMetadata("Test provider", AsOf)));
    }

    private sealed class FakeBondQuoteProvider : IBondQuoteProvider
    {
        public Task<MarketDataEnvelope<BondQuote>?> GetQuoteAsync(
            string market, string identifier, CancellationToken ct) =>
            Task.FromResult<MarketDataEnvelope<BondQuote>?>(
                new MarketDataEnvelope<BondQuote>(
                    new BondQuote(identifier, market, null, "USD",
                        3.75m, null),
                    new MarketDataMetadata("Test provider", AsOf)));
    }
}
