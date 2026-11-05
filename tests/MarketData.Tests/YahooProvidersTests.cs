using System.Net;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public class YahooProvidersTests
{
    private const string Source = "Yahoo Finance (unofficial chart endpoint)";

    private const string ChartBody =
        "{\"chart\":{\"result\":[{\"meta\":{\"symbol\":\"X\",\"currency\":\"USD\"," +
        "\"regularMarketPrice\":105.5,\"chartPreviousClose\":100,\"regularMarketVolume\":42," +
        "\"regularMarketTime\":1700000000,\"longName\":\"Some Name\"}}]}}";

    private static (YahooFinanceClient Client, FakeHttpHandler Handler) CreateClient(
        HttpStatusCode status = HttpStatusCode.OK, string body = ChartBody)
    {
        var handler = FakeHttpHandler.Always(status, body);
        return (new YahooFinanceClient(handler.CreateClient()), handler);
    }

    [Fact]
    public async Task Equity_provider_uppercases_the_ticker_and_maps_the_quote()
    {
        var (client, _) = CreateClient();

        var result = await new YahooEquityQuoteProvider(client)
            .GetQuoteAsync("aapl", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("AAPL", result.Data.Symbol);
        Assert.Equal("Some Name", result.Data.Name);
        Assert.Equal(105.5m, result.Data.Price);
        Assert.Equal("USD", result.Data.Currency);
        Assert.Equal(5.5m, result.Data.ChangePercent);
        Assert.Equal(42L, result.Data.Volume);
        Assert.Equal(Source, result.Metadata.Source);
        Assert.Equal(new DateOnly(2023, 11, 14), result.Metadata.AsOf);
    }

    [Fact]
    public async Task Equity_provider_returns_null_when_the_ticker_is_unknown()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, "{}");

        var result = await new YahooEquityQuoteProvider(client)
            .GetQuoteAsync("NOPE", CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("gold", "GC=F", "Gold futures", "USD per troy ounce")]
    [InlineData("XAU", "GC=F", "Gold futures", "USD per troy ounce")]
    [InlineData("silver", "SI=F", "Silver futures", "USD per troy ounce")]
    [InlineData("XAG", "SI=F", "Silver futures", "USD per troy ounce")]
    [InlineData("WTI", "CL=F", "WTI crude oil futures", "USD per barrel")]
    [InlineData("brent", "BZ=F", "Brent crude oil futures", "USD per barrel")]
    [InlineData("NATGAS", "NG=F", "Natural gas futures", "USD per MMBtu")]
    public async Task Commodity_provider_maps_each_symbol_to_its_yahoo_future(
        string symbol, string yahooSymbol, string name, string unit)
    {
        var (client, handler) = CreateClient();

        var result = await new YahooCommodityQuoteProvider(client)
            .GetQuoteAsync(symbol, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(symbol.ToUpperInvariant(), result.Data.Symbol);
        Assert.Equal(name, result.Data.Name);
        Assert.Equal(unit, result.Data.Unit);
        Assert.Equal(105.5m, result.Data.Price);
        Assert.Equal("USD", result.Data.Currency);
        Assert.Equal(Source, result.Metadata.Source);
        Assert.Equal(new DateOnly(2023, 11, 14), result.Metadata.AsOf);
        Assert.Contains($"/{yahooSymbol}?", Uri.UnescapeDataString(handler.Requests[0]));
    }

    [Fact]
    public async Task Commodity_provider_returns_null_for_unknown_symbols_without_calling_yahoo()
    {
        var (client, handler) = CreateClient();

        var result = await new YahooCommodityQuoteProvider(client)
            .GetQuoteAsync("COPPER", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Commodity_provider_returns_null_when_yahoo_has_no_quote()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, "{}");

        var result = await new YahooCommodityQuoteProvider(client)
            .GetQuoteAsync("GOLD", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Stock_search_provider_delegates_to_the_yahoo_client()
    {
        const string body =
            "{\"quotes\":[{\"quoteType\":\"EQUITY\",\"symbol\":\"AAPL\",\"longname\":\"Apple Inc.\"}]}";
        var (client, handler) = CreateClient(body: body);

        var results = await new YahooStockSearchProvider(client)
            .SearchAsync("apple", CancellationToken.None);

        var single = Assert.Single(results);
        Assert.Equal("AAPL", single.Symbol);
        Assert.Single(handler.Requests);
    }
}
