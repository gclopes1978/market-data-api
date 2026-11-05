using System.Net;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public class EcbEuriborRateProviderTests
{
    private const string Csv = "text/csv";

    private const string ValidCsv =
        "KEY,TIME_PERIOD,OBS_VALUE\n" +
        "FM.M.U2.EUR.RT.MM.EURIBOR3MD_.HSTA,2026-09,2.125\n";

    private static (EcbEuriborRateProvider Provider, FakeHttpHandler Handler) Create(string body)
    {
        var handler = FakeHttpHandler.Sequence(Csv, body);
        return (new EcbEuriborRateProvider(handler.CreateClient("https://ecb.test/service/")), handler);
    }

    [Fact]
    public async Task Parses_the_latest_observation_into_a_rate()
    {
        var (provider, handler) = Create(ValidCsv);

        var result = await provider.GetRateAsync("3M", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("3M", result.Data.Tenor);
        Assert.Equal(2.125m, result.Data.Rate);
        Assert.Equal("Percent per annum", result.Data.Unit);
        Assert.Equal("Monthly average", result.Data.Frequency);
        Assert.Equal("ECB Data Portal (financial market data)", result.Metadata.Source);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Metadata.AsOf);
        Assert.Equal(
            "https://ecb.test/service/data/FM/M.U2.EUR.RT.MM.EURIBOR3MD_.HSTA?format=csvdata&lastNObservations=1",
            handler.Requests.Single());
    }

    [Theory]
    [InlineData("1M", "EURIBOR1MD_")]
    [InlineData("3m", "EURIBOR3MD_")]
    [InlineData("6M", "EURIBOR6MD_")]
    [InlineData("12m", "EURIBOR1YD_")]
    public async Task Maps_each_tenor_to_its_ecb_series_and_normalizes_it(string tenor, string series)
    {
        var (provider, handler) = Create(ValidCsv);

        var result = await provider.GetRateAsync(tenor, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(tenor.ToUpperInvariant(), result.Data.Tenor);
        Assert.Contains($".{series}.", handler.Requests.Single());
    }

    [Fact]
    public async Task Unsupported_tenor_throws_ArgumentException_without_calling_the_ecb()
    {
        var (provider, handler) = Create(ValidCsv);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRateAsync("2M", CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("")]                              // empty body
    [InlineData("KEY,TIME_PERIOD,OBS_VALUE\n")]   // header only
    [InlineData("KEY,TIME_PERIOD,OBS_VALUE\n\n")] // header and a blank line
    public async Task Returns_null_when_there_is_no_observation(string body)
    {
        var (provider, _) = Create(body);

        var result = await provider.GetRateAsync("3M", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Tolerates_a_byte_order_mark_and_quoted_fields_with_commas_and_quotes()
    {
        const string body =
            "\uFEFFKEY,TITLE,TIME_PERIOD,OBS_VALUE\n" +
            "K,\"Euribor \"\"3-month\"\", monthly\",2026-08,2.5\n";
        var (provider, _) = Create(body);

        var result = await provider.GetRateAsync("3M", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2.5m, result.Data.Rate);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Metadata.AsOf);
    }

    [Fact]
    public async Task Uses_the_last_row_and_skips_short_and_blank_lines()
    {
        const string body =
            "KEY,TIME_PERIOD,OBS_VALUE\n" +
            "K,2026-07,2.0\n" +
            "\n" +
            "short\n" +
            "K,2026-08,2.5\n";
        var (provider, _) = Create(body);

        var result = await provider.GetRateAsync("3M", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2.5m, result.Data.Rate);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Metadata.AsOf);
    }

    [Theory]
    [InlineData("KEY,SOMETHING\nabc,1\n")]                          // required columns missing
    [InlineData("KEY,TIME_PERIOD,OBS_VALUE\nK,not-a-month,2.0\n")]  // invalid period
    [InlineData("KEY,TIME_PERIOD,OBS_VALUE\nK,2026-09,abc\n")]      // invalid value
    public async Task Malformed_responses_throw_FormatException(string body)
    {
        var (provider, _) = Create(body);

        await Assert.ThrowsAsync<FormatException>(() =>
            provider.GetRateAsync("3M", CancellationToken.None));
    }

    [Fact]
    public async Task Upstream_error_throws_HttpRequestException()
    {
        var handler = new FakeHttpHandler((_, _) =>
            FakeHttpHandler.Respond(HttpStatusCode.InternalServerError, "boom", Csv));
        var provider = new EcbEuriborRateProvider(handler.CreateClient());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetRateAsync("3M", CancellationToken.None));
    }
}
