using System.Net;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public class YahooFinanceClientTests
{
    private static YahooFinanceClient Create(FakeHttpHandler handler) =>
        new(handler.CreateClient());

    private static YahooFinanceClient CreateWith(
        string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Create(FakeHttpHandler.Always(status, body));

    // Builds a Yahoo chart payload: "meta" content plus optional sibling properties of the result.
    private static string Chart(string meta, string siblings = "") =>
        "{\"chart\":{\"result\":[{\"meta\":{" + meta + "}" + siblings + "}]}}";

    private const string FullMeta =
        "\"symbol\":\"AAPL\",\"currency\":\"USD\",\"regularMarketPrice\":110.0," +
        "\"chartPreviousClose\":100.0,\"regularMarketVolume\":123456," +
        "\"regularMarketTime\":1700000000,\"longName\":\"Apple Inc.\"";

    [Fact]
    public async Task GetQuote_maps_a_complete_payload()
    {
        var quote = await CreateWith(Chart(FullMeta))
            .GetQuoteAsync("AAPL", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal("Apple Inc.", quote.Name);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal(110m, quote.Price);
        Assert.Equal(10m, quote.ChangePercent);
        Assert.Equal(123456L, quote.Volume);
        Assert.Equal(new DateOnly(2023, 11, 14), quote.AsOf);
    }

    [Fact]
    public async Task GetQuote_requests_the_five_day_daily_chart_for_the_symbol()
    {
        var handler = FakeHttpHandler.Always(HttpStatusCode.OK, Chart(FullMeta));

        await Create(handler).GetQuoteAsync("^GSPC", CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Contains(
            "v8/finance/chart/^GSPC?range=5d&interval=1d",
            Uri.UnescapeDataString(handler.Requests[0]));
    }

    [Fact]
    public async Task GetQuote_returns_null_when_yahoo_answers_404()
    {
        var quote = await CreateWith("{}", HttpStatusCode.NotFound)
            .GetQuoteAsync("NOPE", CancellationToken.None);

        Assert.Null(quote);
    }

    [Fact]
    public async Task GetQuote_throws_HttpRequestException_on_server_errors()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateWith("boom", HttpStatusCode.InternalServerError)
                .GetQuoteAsync("AAPL", CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]                                          // no chart
    [InlineData("{\"chart\":{}}")]                              // no result
    [InlineData("{\"chart\":{\"result\":null}}")]               // result is not an array
    [InlineData("{\"chart\":{\"result\":[]}}")]                 // empty result
    [InlineData("{\"chart\":{\"result\":[{}]}}")]               // no meta
    public async Task GetQuote_returns_null_for_payloads_without_usable_data(string body)
    {
        var quote = await CreateWith(body)
            .GetQuoteAsync("AAPL", CancellationToken.None);

        Assert.Null(quote);
    }

    [Theory]
    [InlineData("\"regularMarketPrice\":10.0,\"regularMarketTime\":1700000000")]                  // no currency
    [InlineData("\"currency\":\"  \",\"regularMarketPrice\":10.0,\"regularMarketTime\":1700000000")] // blank currency
    [InlineData("\"currency\":\"USD\",\"regularMarketTime\":1700000000")]                          // no price anywhere
    [InlineData("\"currency\":\"USD\",\"regularMarketPrice\":10.0")]                               // no timestamp anywhere
    public async Task GetQuote_returns_null_when_a_required_field_is_missing(string meta)
    {
        var quote = await CreateWith(Chart(meta))
            .GetQuoteAsync("AAPL", CancellationToken.None);

        Assert.Null(quote);
    }

    [Fact]
    public async Task GetQuote_falls_back_to_the_last_numeric_close_when_price_is_missing()
    {
        var body = Chart(
            "\"currency\":\"EUR\",\"regularMarketTime\":1700000000",
            ",\"indicators\":{\"quote\":[{\"close\":[1.5,null,2.5,null]}]}");

        var quote = await CreateWith(body).GetQuoteAsync("ABC", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(2.5m, quote.Price);
    }

    [Fact]
    public async Task GetQuote_returns_null_when_no_close_is_numeric()
    {
        var body = Chart(
            "\"currency\":\"EUR\",\"regularMarketTime\":1700000000",
            ",\"indicators\":{\"quote\":[{\"close\":[null,null]}]}");

        var quote = await CreateWith(body).GetQuoteAsync("ABC", CancellationToken.None);

        Assert.Null(quote);
    }

    [Fact]
    public async Task GetQuote_falls_back_to_the_last_timestamp_when_market_time_is_missing()
    {
        var body = Chart(
            "\"currency\":\"USD\",\"regularMarketPrice\":10.0",
            ",\"timestamp\":[1700000000,1700086400]");

        var quote = await CreateWith(body).GetQuoteAsync("ABC", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(new DateOnly(2023, 11, 15), quote.AsOf);
    }

    [Fact]
    public async Task GetQuote_uses_previousClose_when_chartPreviousClose_is_missing()
    {
        var meta =
            "\"currency\":\"USD\",\"regularMarketPrice\":210.0,\"previousClose\":200.0," +
            "\"regularMarketTime\":1700000000";

        var quote = await CreateWith(Chart(meta)).GetQuoteAsync("ABC", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(5m, quote.ChangePercent);
    }

    [Theory]
    [InlineData("")]                                // no previous close at all
    [InlineData("\"chartPreviousClose\":0,")]       // a zero previous close cannot be a divisor
    public async Task GetQuote_falls_back_to_the_reported_change_percent(string previous)
    {
        var meta =
            "\"currency\":\"USD\",\"regularMarketPrice\":10.0," + previous +
            "\"regularMarketChangePercent\":1.25,\"regularMarketTime\":1700000000";

        var quote = await CreateWith(Chart(meta)).GetQuoteAsync("ABC", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal(1.25m, quote.ChangePercent);
    }

    [Fact]
    public async Task GetQuote_leaves_optional_fields_empty_when_absent()
    {
        var meta = "\"currency\":\"USD\",\"regularMarketPrice\":10.0,\"regularMarketTime\":1700000000";

        var quote = await CreateWith(Chart(meta)).GetQuoteAsync("msft", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("msft", quote.Symbol);   // falls back to the requested symbol
        Assert.Null(quote.Name);
        Assert.Null(quote.ChangePercent);
        Assert.Null(quote.Volume);
    }

    [Fact]
    public async Task GetQuote_uses_shortName_when_longName_is_missing()
    {
        var meta =
            "\"currency\":\"USD\",\"regularMarketPrice\":10.0,\"regularMarketTime\":1700000000," +
            "\"shortName\":\"Short Name\"";

        var quote = await CreateWith(Chart(meta)).GetQuoteAsync("ABC", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("Short Name", quote.Name);
    }

    private const string SearchBody = """
        {"quotes":[
          {"quoteType":"EQUITY","symbol":"AAPL","longname":"Apple Inc.","shortname":"Apple","exchange":"NMS","exchDisp":"NASDAQ"},
          {"quoteType":"equity","symbol":"MSFT","shortname":"Microsoft Corp"},
          {"quoteType":"ETF","symbol":"SPY","longname":"SPDR S&P 500"},
          {"quoteType":"EQUITY","longname":"Has no symbol"},
          {"quoteType":"EQUITY","symbol":"NONAME"}
        ]}
        """;

    [Fact]
    public async Task SearchStocks_keeps_only_equities_with_symbol_and_name()
    {
        var results = await CreateWith(SearchBody)
            .SearchStocksAsync("apple", CancellationToken.None);

        Assert.Equal(2, results.Count);

        Assert.Equal("AAPL", results[0].Symbol);
        Assert.Equal("Apple Inc.", results[0].Name);
        Assert.Equal("NMS", results[0].Exchange);
        Assert.Equal("NASDAQ", results[0].ExchangeName);

        Assert.Equal("MSFT", results[1].Symbol);
        Assert.Equal("Microsoft Corp", results[1].Name);   // shortname fallback
        Assert.Null(results[1].Exchange);
        Assert.Null(results[1].ExchangeName);
    }

    [Fact]
    public async Task SearchStocks_escapes_the_query()
    {
        var handler = FakeHttpHandler.Always(HttpStatusCode.OK, SearchBody);

        await Create(handler).SearchStocksAsync("apple inc", CancellationToken.None);

        Assert.Contains(
            "v1/finance/search?q=apple inc&quotesCount=100&newsCount=0",
            Uri.UnescapeDataString(handler.Requests[0]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"quotes\":\"nope\"}")]
    [InlineData("{\"quotes\":[]}")]
    public async Task SearchStocks_returns_an_empty_list_when_there_are_no_quotes(string body)
    {
        var results = await CreateWith(body).SearchStocksAsync("abc", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchStocks_throws_HttpRequestException_on_server_errors()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateWith("boom", HttpStatusCode.BadGateway)
                .SearchStocksAsync("abc", CancellationToken.None));
    }
}
