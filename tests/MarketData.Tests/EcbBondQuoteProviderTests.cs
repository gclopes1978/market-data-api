using System.Net;
using System.Text;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public class EcbBondQuoteProviderTests
{
    // Returns a fixed response and remembers how many calls were made and the last URL.
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/csv")
            });
        }
    }

    private static EcbBondQuoteProvider Create(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/service/") });

    // A trimmed ECB CSV. The TITLE field contains a comma inside quotes, like the real one.
    private const string Csv =
        "KEY,FREQ,TITLE,TIME_PERIOD,OBS_VALUE\n" +
        "YC.B.U2.EUR.4F.G_N_A.SV_C_YM.SR_10Y,B,\"Yield curve spot rate, 10-year maturity\",2026-10-27,2.912345\n";

    [Fact]
    public async Task Parses_latest_observation_into_a_yield()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Csv);

        var quote = await Create(handler).GetQuoteAsync("EUR", "eur10y", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("EUR10Y", quote.Data.Identifier);
        Assert.Equal("EUR", quote.Data.Market);
        Assert.Equal(2.912345m, quote.Data.Yield);
        Assert.Null(quote.Data.Price);
        Assert.Equal(new DateOnly(2026, 10, 27), quote.Metadata.AsOf);
        Assert.Equal(
            "https://example.test/service/data/YC/B.U2.EUR.4F.G_N_A.SV_C_YM.SR_10Y?format=csvdata&lastNObservations=1",
            handler.LastUri);
    }

    [Theory]
    [InlineData("US", "EUR10Y")]   // wrong market
    [InlineData("EUR", "EUR4Y")]   // maturity not in the ECB dataset
    public async Task Unknown_market_or_maturity_returns_null_without_calling_the_ecb(
        string market, string identifier)
    {
        var handler = new StubHandler(HttpStatusCode.OK, Csv);

        var quote = await Create(handler).GetQuoteAsync(market, identifier, CancellationToken.None);

        Assert.Null(quote);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Header_only_response_returns_null()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "KEY,TIME_PERIOD,OBS_VALUE\n");

        var quote = await Create(handler).GetQuoteAsync("EUR", "EUR10Y", CancellationToken.None);

        Assert.Null(quote);
    }

    [Fact]
    public async Task Missing_columns_throw_FormatException()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "KEY,SOMETHING\nabc,1\n");

        await Assert.ThrowsAsync<FormatException>(() =>
            Create(handler).GetQuoteAsync("EUR", "EUR10Y", CancellationToken.None));
    }

    [Fact]
    public async Task Upstream_error_throws_HttpRequestException()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(handler).GetQuoteAsync("EUR", "EUR10Y", CancellationToken.None));
    }
}
