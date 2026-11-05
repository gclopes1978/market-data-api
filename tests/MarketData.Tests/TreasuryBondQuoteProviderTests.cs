using System.Net;
using System.Xml;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public class TreasuryBondQuoteProviderTests
{
    private const string Xml = "application/xml";

    private static string Entry(string date, string field, string value) =>
        "<entry><content type=\"application/xml\"><m:properties>" +
        $"<d:NEW_DATE m:type=\"Edm.DateTime\">{date}</d:NEW_DATE>" +
        $"<d:{field} m:type=\"Edm.Double\">{value}</d:{field}>" +
        "</m:properties></content></entry>";

    private static string Feed(params string[] entries) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<feed xmlns=\"http://www.w3.org/2005/Atom\" " +
        "xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" " +
        "xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
        string.Concat(entries) + "</feed>";

    private static TreasuryBondQuoteProvider Create(FakeHttpHandler handler) =>
        new(handler.CreateClient("https://treasury.test/"));

    // Midday timestamps keep the expected date stable in any local time zone.
    private static string TenYearFeed(string date = "2026-10-30T12:00:00", string value = "4.12") =>
        Feed(Entry(date, "BC_10YEAR", value));

    [Fact]
    public async Task Returns_the_par_yield_for_the_requested_maturity()
    {
        var handler = FakeHttpHandler.Sequence(Xml, TenYearFeed());

        var quote = await Create(handler).GetQuoteAsync("US", "US10Y", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("US10Y", quote.Data.Identifier);
        Assert.Equal("US", quote.Data.Market);
        Assert.Null(quote.Data.Price);
        Assert.Equal("USD", quote.Data.Currency);
        Assert.Equal(4.12m, quote.Data.Yield);
        Assert.Null(quote.Data.MaturityDate);
        Assert.Equal("U.S. Treasury Daily Treasury Par Yield Curve", quote.Metadata.Source);
        Assert.Equal(new DateOnly(2026, 10, 30), quote.Metadata.AsOf);
        Assert.Single(handler.Requests);
        Assert.Contains("data=daily_treasury_yield_curve", handler.Requests[0]);
    }

    [Fact]
    public async Task Accepts_lowercase_market_and_identifier()
    {
        var handler = FakeHttpHandler.Sequence(Xml, TenYearFeed());

        var quote = await Create(handler).GetQuoteAsync("us", "us10y", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("US10Y", quote.Data.Identifier);
        Assert.Equal("US", quote.Data.Market);
    }

    [Theory]
    [InlineData("US1M", "BC_1MONTH")]
    [InlineData("US3M", "BC_3MONTH")]
    [InlineData("US6M", "BC_6MONTH")]
    [InlineData("US1Y", "BC_1YEAR")]
    [InlineData("US2Y", "BC_2YEAR")]
    [InlineData("US5Y", "BC_5YEAR")]
    [InlineData("US7Y", "BC_7YEAR")]
    [InlineData("US10Y", "BC_10YEAR")]
    [InlineData("US20Y", "BC_20YEAR")]
    [InlineData("US30Y", "BC_30YEAR")]
    public async Task Maps_each_maturity_to_its_treasury_field(string identifier, string field)
    {
        var handler = FakeHttpHandler.Sequence(
            Xml, Feed(Entry("2026-10-30T12:00:00", field, "3.21")));

        var quote = await Create(handler).GetQuoteAsync("US", identifier, CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(3.21m, quote.Data.Yield);
    }

    [Theory]
    [InlineData("US", "US99Y")]    // maturity the Treasury curve does not have
    [InlineData("EUR", "US10Y")]   // not this provider's market
    public async Task Unsupported_market_or_maturity_returns_null_without_calling_treasury(
        string market, string identifier)
    {
        var handler = FakeHttpHandler.Sequence(Xml, TenYearFeed());

        var quote = await Create(handler).GetQuoteAsync(market, identifier, CancellationToken.None);

        Assert.Null(quote);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Falls_back_to_the_previous_month_when_the_current_one_has_no_rows()
    {
        var handler = FakeHttpHandler.Sequence(Xml, Feed(), TenYearFeed());

        var quote = await Create(handler).GetQuoteAsync("US", "US10Y", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(2, handler.Calls);
        Assert.NotEqual(handler.Requests[0], handler.Requests[1]);
    }

    [Fact]
    public async Task Gives_up_after_three_months_without_data()
    {
        var handler = FakeHttpHandler.Sequence(Xml, Feed());

        var quote = await Create(handler).GetQuoteAsync("US", "US10Y", CancellationToken.None);

        Assert.Null(quote);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Skips_rows_with_an_invalid_date_or_yield_and_uses_the_next_valid_one()
    {
        var handler = FakeHttpHandler.Sequence(Xml, Feed(
            Entry("not-a-date", "BC_10YEAR", "4.10"),
            Entry("2026-10-29T12:00:00", "BC_10YEAR", "N/A"),
            Entry("2026-10-28T12:00:00", "BC_10YEAR", "4.05")));

        var quote = await Create(handler).GetQuoteAsync("US", "US10Y", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(4.05m, quote.Data.Yield);
        Assert.Equal(new DateOnly(2026, 10, 28), quote.Metadata.AsOf);
    }

    [Fact]
    public async Task Upstream_error_throws_HttpRequestException()
    {
        var handler = new FakeHttpHandler((_, _) =>
            FakeHttpHandler.Respond(HttpStatusCode.ServiceUnavailable, "down", Xml));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(handler).GetQuoteAsync("US", "US10Y", CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_xml_throws_XmlException()
    {
        var handler = FakeHttpHandler.Sequence(Xml, "this is not xml");

        await Assert.ThrowsAsync<XmlException>(() =>
            Create(handler).GetQuoteAsync("US", "US10Y", CancellationToken.None));
    }
}
