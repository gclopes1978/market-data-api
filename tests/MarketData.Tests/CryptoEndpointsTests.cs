using System.Net;
using System.Text.Json;
using MarketData.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarketData.Tests;

public sealed class CryptoEndpointsTests(
    WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly MarketDataMetadata Metadata =
        new("CoinPaprika", new DateOnly(2026, 10, 29));

    private static readonly CryptoAsset[] Assets =
    [
        new("btc-bitcoin", "Bitcoin", "BTC", 1, "coin"),
        new("eth-ethereum", "Ethereum", "ETH", 2, "coin"),
        new("bch-bitcoin-cash", "Bitcoin Cash", "BCH", 18, "coin"),
        new("wbtc-wrapped-bitcoin", "Wrapped Bitcoin", "WBTC", 15, "token"),
        new("btc-bitcoin-fake", "Fake Coin", "BTC", 900, "token"),
        new("new-newcoin", "Newcoin", "NEW", null, "token"),
    ];

    private sealed class FakeCryptoCatalogProvider(
        Func<CancellationToken, Task<MarketDataEnvelope<IReadOnlyList<CryptoAsset>>>>? handler = null)
        : ICryptoCatalogProvider
    {
        public Task<MarketDataEnvelope<IReadOnlyList<CryptoAsset>>> GetAssetsAsync(
            CancellationToken ct) =>
            handler is not null
                ? handler(ct)
                : Task.FromResult(
                    new MarketDataEnvelope<IReadOnlyList<CryptoAsset>>(Assets, Metadata));
    }

    private sealed class FakeCryptoQuoteProvider(
        Func<string, string, Task<MarketDataEnvelope<CryptoQuote>?>>? handler = null)
        : ICryptoQuoteProvider
    {
        public string? LastId { get; private set; }
        public string? LastCurrency { get; private set; }

        public Task<MarketDataEnvelope<CryptoQuote>?> GetQuoteAsync(
            string id, string currency, CancellationToken ct)
        {
            LastId = id;
            LastCurrency = currency;

            return handler is not null
                ? handler(id, currency)
                : Task.FromResult<MarketDataEnvelope<CryptoQuote>?>(
                    new MarketDataEnvelope<CryptoQuote>(
                        new CryptoQuote(
                            id, "BTC", "Bitcoin", 93500.25m, currency,
                            -1.37m, 31000000000m, 1850000000000m, 1),
                        Metadata));
        }
    }

    private HttpClient CreateTestClient(
        ICryptoCatalogProvider? catalog = null,
        ICryptoQuoteProvider? quotes = null)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICryptoCatalogProvider>();
                services.AddSingleton(catalog ?? new FakeCryptoCatalogProvider());

                services.RemoveAll<ICryptoQuoteProvider>();
                services.AddSingleton(quotes ?? new FakeCryptoQuoteProvider());
            })).CreateClient();
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string[] Ids(JsonDocument document) =>
        document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToArray();

    // ---- GET /api/crypto ----------------------------------------------------------------

    [Fact]
    public async Task List_returns_every_asset_with_metadata()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/crypto");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJson(response);
        var root = document.RootElement;

        Assert.Equal(Assets.Length, root.GetProperty("data").GetArrayLength());
        Assert.Equal("CoinPaprika", root.GetProperty("metadata").GetProperty("source").GetString());
        Assert.Equal("2026-10-29", root.GetProperty("metadata").GetProperty("asOf").GetString());

        var first = root.GetProperty("data")[0];
        Assert.Equal("btc-bitcoin", first.GetProperty("id").GetString());
        Assert.Equal("Bitcoin", first.GetProperty("name").GetString());
        Assert.Equal("BTC", first.GetProperty("symbol").GetString());
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task List_filters_by_name_or_symbol_ignoring_case_and_puts_exact_symbol_first()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/crypto?q=btc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJson(response);

        // Exact symbol matches first (catalog order kept), then the other matches.
        // "Bitcoin Cash" (BCH) is not a match: "btc" is neither in its name nor in its symbol.
        var ids = Ids(document);
        Assert.Equal(
            new[] { "btc-bitcoin", "btc-bitcoin-fake", "wbtc-wrapped-bitcoin" },
            ids);
        Assert.DoesNotContain("eth-ethereum", ids);
    }

    [Fact]
    public async Task List_applies_the_limit()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/crypto?limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJson(response);
        Assert.Equal(new[] { "btc-bitcoin", "eth-ethereum" }, Ids(document));
    }

    [Fact]
    public async Task List_applies_the_limit_after_filtering()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/crypto?q=bitcoin&limit=1");

        using var document = await ReadJson(response);
        Assert.Equal(new[] { "btc-bitcoin" }, Ids(document));
    }

    [Fact]
    public async Task List_returns_an_empty_array_when_nothing_matches()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync("/api/crypto?q=zzzzzz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJson(response);
        Assert.Equal(0, document.RootElement.GetProperty("data").GetArrayLength());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1001")]
    public async Task List_rejects_an_out_of_range_limit(string limit)
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync($"/api/crypto?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_rejects_a_too_long_query()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync($"/api/crypto?q={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task List_maps_provider_failures(Exception failure, HttpStatusCode expected)
    {
        using var client = CreateTestClient(
            catalog: new FakeCryptoCatalogProvider(
                _ => Task.FromException<MarketDataEnvelope<IReadOnlyList<CryptoAsset>>>(failure)));

        using var response = await client.GetAsync("/api/crypto");

        Assert.Equal(expected, response.StatusCode);
    }

    // ---- GET /api/crypto/{id} -----------------------------------------------------------

    [Fact]
    public async Task Quote_returns_the_quote_and_metadata()
    {
        var provider = new FakeCryptoQuoteProvider();
        using var client = CreateTestClient(quotes: provider);

        using var response = await client.GetAsync("/api/crypto/btc-bitcoin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJson(response);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("btc-bitcoin", data.GetProperty("id").GetString());
        Assert.Equal("BTC", data.GetProperty("symbol").GetString());
        Assert.Equal("Bitcoin", data.GetProperty("name").GetString());
        Assert.Equal(93500.25m, data.GetProperty("price").GetDecimal());
        Assert.Equal("USD", data.GetProperty("currency").GetString());
        Assert.Equal(-1.37m, data.GetProperty("changePercent24h").GetDecimal());
        Assert.Equal(31000000000m, data.GetProperty("volume24h").GetDecimal());
        Assert.Equal(1850000000000m, data.GetProperty("marketCap").GetDecimal());
        Assert.Equal(1, data.GetProperty("rank").GetInt32());
        Assert.Equal("CoinPaprika",
            document.RootElement.GetProperty("metadata").GetProperty("source").GetString());
    }

    [Fact]
    public async Task Quote_defaults_to_usd_and_normalises_the_id()
    {
        var provider = new FakeCryptoQuoteProvider();
        using var client = CreateTestClient(quotes: provider);

        using var response = await client.GetAsync("/api/crypto/BTC-Bitcoin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("btc-bitcoin", provider.LastId);
        Assert.Equal("USD", provider.LastCurrency);
    }

    [Fact]
    public async Task Quote_accepts_eur_in_any_case()
    {
        var provider = new FakeCryptoQuoteProvider();
        using var client = CreateTestClient(quotes: provider);

        using var response = await client.GetAsync("/api/crypto/btc-bitcoin?currency=eur");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("EUR", provider.LastCurrency);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("BTC")]
    [InlineData("12345")]
    public async Task Quote_rejects_an_unsupported_currency(string currency)
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync(
            $"/api/crypto/btc-bitcoin?currency={currency}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("btc%20bitcoin")]
    [InlineData("btc$bitcoin")]
    [InlineData("btc:bitcoin")]
    public async Task Quote_rejects_an_invalid_id(string id)
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync($"/api/crypto/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quote_rejects_a_too_long_id()
    {
        using var client = CreateTestClient();
        using var response = await client.GetAsync($"/api/crypto/{new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quote_returns_404_when_the_coin_is_unknown()
    {
        using var client = CreateTestClient(
            quotes: new FakeCryptoQuoteProvider(
                (_, _) => Task.FromResult<MarketDataEnvelope<CryptoQuote>?>(null)));

        using var response = await client.GetAsync("/api/crypto/nope-nothing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task Quote_maps_provider_failures(Exception failure, HttpStatusCode expected)
    {
        using var client = CreateTestClient(
            quotes: new FakeCryptoQuoteProvider(
                (_, _) => Task.FromException<MarketDataEnvelope<CryptoQuote>?>(failure)));

        using var response = await client.GetAsync("/api/crypto/btc-bitcoin");

        Assert.Equal(expected, response.StatusCode);
    }

    public static TheoryData<Exception, HttpStatusCode> ProviderFailures() => new()
    {
        { new HttpRequestException("Simulated provider failure"), HttpStatusCode.BadGateway },
        { new JsonException("Simulated invalid JSON"), HttpStatusCode.BadGateway },
        { new FormatException("Simulated invalid payload"), HttpStatusCode.BadGateway },
        { new OperationCanceledException("Simulated timeout"), HttpStatusCode.GatewayTimeout },
    };
}
