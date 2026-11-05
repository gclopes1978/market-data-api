using System.Net;
using System.Text.Json;
using System.Xml;
using MarketData.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarketData.Tests;

/// <summary>
/// Error mapping and input validation of the market endpoints: every provider failure must be
/// translated into the documented status code, never leak as an unhandled 500.
/// </summary>
public sealed class MarketEndpointsErrorTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // One stub implements every market provider: it throws `failure`, or answers "nothing found".
    private sealed class StubProviders(Exception? failure) :
        IEuriborRateProvider, IBondQuoteProvider, IStockSearchProvider,
        IEquityQuoteProvider, ICommodityQuoteProvider
    {
        Task<MarketDataEnvelope<EuriborRate>?> IEuriborRateProvider.GetRateAsync(
            string tenor, CancellationToken ct) =>
            Result<MarketDataEnvelope<EuriborRate>>();

        Task<MarketDataEnvelope<BondQuote>?> IBondQuoteProvider.GetQuoteAsync(
            string market, string identifier, CancellationToken ct) =>
            Result<MarketDataEnvelope<BondQuote>>();

        Task<MarketDataEnvelope<EquityQuote>?> IEquityQuoteProvider.GetQuoteAsync(
            string ticker, CancellationToken ct) =>
            Result<MarketDataEnvelope<EquityQuote>>();

        Task<MarketDataEnvelope<CommodityQuote>?> ICommodityQuoteProvider.GetQuoteAsync(
            string symbol, CancellationToken ct) =>
            Result<MarketDataEnvelope<CommodityQuote>>();

        Task<IReadOnlyList<StockSearchResult>> IStockSearchProvider.SearchAsync(
            string query, CancellationToken ct) =>
            failure is null
                ? Task.FromResult<IReadOnlyList<StockSearchResult>>(Array.Empty<StockSearchResult>())
                : Task.FromException<IReadOnlyList<StockSearchResult>>(failure);

        private Task<T?> Result<T>() where T : class =>
            failure is null
                ? Task.FromResult<T?>(null)
                : Task.FromException<T?>(failure);
    }

    private HttpClient CreateClient(Exception? failure)
    {
        var stub = new StubProviders(failure);

        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEuriborRateProvider>();
                services.AddSingleton<IEuriborRateProvider>(stub);
                services.RemoveAll<IBondQuoteProvider>();
                services.AddSingleton<IBondQuoteProvider>(stub);
                services.RemoveAll<IStockSearchProvider>();
                services.AddSingleton<IStockSearchProvider>(stub);
                services.RemoveAll<IEquityQuoteProvider>();
                services.AddSingleton<IEquityQuoteProvider>(stub);
                services.RemoveAll<ICommodityQuoteProvider>();
                services.AddSingleton<ICommodityQuoteProvider>(stub);
            })).CreateClient();
    }

    private static Exception FailureOf(string kind) => kind switch
    {
        "http" => new HttpRequestException("upstream down"),
        "json" => new JsonException("bad json"),
        "format" => new FormatException("bad format"),
        "xml" => new XmlException("bad xml"),
        "timeout" => new OperationCanceledException("upstream timeout"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static TheoryData<string, string, HttpStatusCode> FailureCases()
    {
        var data = new TheoryData<string, string, HttpStatusCode>();

        void Add(string path, params string[] kinds)
        {
            foreach (var kind in kinds)
            {
                data.Add(path, kind, kind == "timeout"
                    ? HttpStatusCode.GatewayTimeout
                    : HttpStatusCode.BadGateway);
            }
        }

        string[] everything = ["http", "json", "format", "xml", "timeout"];
        string[] withoutXml = ["http", "json", "format", "timeout"];

        // Only the Euribor and bond endpoints parse XML/CSV, so only they map XmlException.
        Add("/api/euribor/3M", everything);
        Add("/api/bonds/US/US10Y", everything);
        Add("/api/bonds/EUR/EUR10Y", everything);
        Add("/api/stocks/search?q=ab", withoutXml);
        Add("/api/stocks/AAPL", withoutXml);
        Add("/api/etfs/SPY", withoutXml);
        Add("/api/commodities/GOLD", withoutXml);

        return data;
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task Provider_failures_are_mapped_to_gateway_errors(
        string path, string kind, HttpStatusCode expected)
    {
        using var client = CreateClient(FailureOf(kind));

        var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/euribor/3M")]
    [InlineData("/api/bonds/US/US10Y")]
    [InlineData("/api/bonds/EUR/EUR10Y")]
    [InlineData("/api/stocks/AAPL")]
    [InlineData("/api/etfs/SPY")]
    [InlineData("/api/commodities/GOLD")]
    public async Task Unknown_instruments_return_404(string path)
    {
        using var client = CreateClient(failure: null);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stock_search_without_matches_returns_an_empty_list()
    {
        using var client = CreateClient(failure: null);

        var response = await client.GetAsync("/api/stocks/search?q=zz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/bonds/XX/US10Y", "Invalid bond market")]
    [InlineData("/api/bonds/US/bad_id", "Invalid bond identifier")]
    [InlineData("/api/bonds/US/US99Y", "Supported US Treasury maturities")]
    [InlineData("/api/bonds/EUR/EUR4Y", "Supported euro area maturities")]
    [InlineData("/api/stocks/search", "Invalid stock search query")]
    [InlineData("/api/stocks/search?q=a", "at least 2 characters")]
    [InlineData("/api/stocks/search?q=%20%20a%20", "at least 2 characters")]
    [InlineData("/api/euribor/2M", "Supported tenors are 1M, 3M, 6M and 12M")]
    public async Task Invalid_input_returns_400_with_an_explanation(string path, string expectedText)
    {
        using var client = CreateClient(failure: null);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedText, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Overlong_stock_search_query_returns_400()
    {
        using var client = CreateClient(failure: null);

        var response = await client.GetAsync($"/api/stocks/search?q={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must not exceed 100 characters", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("stocks", "Invalid stock ticker")]
    [InlineData("etfs", "Invalid etf ticker")]
    public async Task Overlong_tickers_are_rejected_with_the_asset_name(string route, string expectedTitle)
    {
        using var client = CreateClient(failure: null);

        var response = await client.GetAsync($"/api/{route}/{new string('A', 21)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedTitle, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Overlong_bond_identifiers_are_rejected()
    {
        using var client = CreateClient(failure: null);

        var response = await client.GetAsync($"/api/bonds/US/{new string('A', 33)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
