using MarketData.Infrastructure;

namespace MarketData.Tests;

public class CompositeBondQuoteProviderTests
{
    private const string EcbCsv =
        "KEY,FREQ,TITLE,TIME_PERIOD,OBS_VALUE\n" +
        "YC.B.U2.EUR.4F.G_N_A.SV_C_YM.SR_10Y,B,\"Yield curve spot rate, 10-year maturity\",2026-10-27,2.912345\n";

    private const string EmptyTreasuryFeed =
        "<feed xmlns=\"http://www.w3.org/2005/Atom\"></feed>";

    private static (CompositeBondQuoteProvider Provider, FakeHttpHandler Treasury, FakeHttpHandler Ecb) Create()
    {
        var treasury = FakeHttpHandler.Sequence("application/xml", EmptyTreasuryFeed);
        var ecb = FakeHttpHandler.Sequence("text/csv", EcbCsv);

        var provider = new CompositeBondQuoteProvider(
            new TreasuryBondQuoteProvider(treasury.CreateClient("https://treasury.test/")),
            new EcbBondQuoteProvider(ecb.CreateClient("https://ecb.test/service/")));

        return (provider, treasury, ecb);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("us")]
    public async Task Routes_the_us_market_to_the_treasury_provider(string market)
    {
        var (provider, treasury, ecb) = Create();

        await provider.GetQuoteAsync(market, "US10Y", CancellationToken.None);

        Assert.True(treasury.Calls > 0);
        Assert.Equal(0, ecb.Calls);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("eur")]
    public async Task Routes_the_eur_market_to_the_ecb_provider(string market)
    {
        var (provider, treasury, ecb) = Create();

        var quote = await provider.GetQuoteAsync(market, "EUR10Y", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(2.912345m, quote.Data.Yield);
        Assert.Equal(1, ecb.Calls);
        Assert.Equal(0, treasury.Calls);
    }

    [Fact]
    public async Task Markets_without_a_provider_return_null_and_call_nobody()
    {
        var (provider, treasury, ecb) = Create();

        var quote = await provider.GetQuoteAsync("GLOBAL", "US10Y", CancellationToken.None);

        Assert.Null(quote);
        Assert.Equal(0, treasury.Calls);
        Assert.Equal(0, ecb.Calls);
    }
}
