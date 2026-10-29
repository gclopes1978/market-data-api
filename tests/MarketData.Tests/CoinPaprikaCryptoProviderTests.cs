using System.Net;
using System.Text;
using MarketData.Core;
using MarketData.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace MarketData.Tests;

public class CoinPaprikaCryptoProviderTests
{
    private const string CoinsJson = """
        [
          {"id":"eth-ethereum","name":"Ethereum","symbol":"ETH","rank":2,"is_new":false,"is_active":true,"type":"coin"},
          {"id":"dead-deadcoin","name":"Deadcoin","symbol":"DEAD","rank":5,"is_new":false,"is_active":false,"type":"coin"},
          {"id":"btc-bitcoin","name":"Bitcoin","symbol":"BTC","rank":1,"is_new":false,"is_active":true,"type":"coin"},
          {"id":"zzz-zebra","name":"Zebra","symbol":"ZZZ","rank":0,"is_new":true,"is_active":true,"type":"token"},
          {"id":"aaa-alpha","name":"Alpha","symbol":"AAA","rank":0,"is_new":true,"is_active":true,"type":"token"},
          {"id":"","name":"No id","symbol":"NOID","rank":9,"is_new":false,"is_active":true,"type":"coin"}
        ]
        """;

    private const string TickerJson = """
        {
          "id":"btc-bitcoin","name":"Bitcoin","symbol":"BTC","rank":1,
          "last_updated":"2026-10-29T10:47:48Z",
          "quotes":{"EUR":{"price":93500.25,"volume_24h":31000000000.5,"market_cap":1850000000000,"percent_change_24h":-1.37}}
        }
        """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static CoinPaprikaCryptoProvider Create(StubHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/v1/")
        }, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Catalog_keeps_active_coins_ordered_by_rank_with_unranked_last()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CoinsJson);

        var result = await Create(handler).GetAssetsAsync(CancellationToken.None);

        Assert.Equal("https://example.test/v1/coins", handler.LastUri);
        Assert.Equal("CoinPaprika", result.Metadata.Source);
        Assert.Equal(
            new[] { "btc-bitcoin", "eth-ethereum", "aaa-alpha", "zzz-zebra" },
            result.Data.Select(a => a.Id).ToArray());

        var bitcoin = result.Data[0];
        Assert.Equal("Bitcoin", bitcoin.Name);
        Assert.Equal("BTC", bitcoin.Symbol);
        Assert.Equal(1, bitcoin.Rank);
        Assert.Equal("coin", bitcoin.Type);

        // Rank 0 means "unranked" and is exposed as null.
        Assert.Null(result.Data[2].Rank);
    }

    [Fact]
    public async Task Catalog_is_cached_between_calls()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CoinsJson);
        var provider = Create(handler);

        await provider.GetAssetsAsync(CancellationToken.None);
        await provider.GetAssetsAsync(CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Catalog_throws_when_upstream_fails()
    {
        var provider = Create(new StubHandler(HttpStatusCode.TooManyRequests, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetAssetsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_throws_when_body_is_null()
    {
        var provider = Create(new StubHandler(HttpStatusCode.OK, "null"));

        await Assert.ThrowsAsync<FormatException>(() =>
            provider.GetAssetsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Quote_parses_upstream_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK, TickerJson);

        var result = await Create(handler).GetQuoteAsync(
            "BTC-Bitcoin", "eur", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(
            "https://example.test/v1/tickers/btc-bitcoin?quotes=EUR",
            handler.LastUri);

        var quote = result.Data;
        Assert.Equal("btc-bitcoin", quote.Id);
        Assert.Equal("BTC", quote.Symbol);
        Assert.Equal("Bitcoin", quote.Name);
        Assert.Equal(93500.25m, quote.Price);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal(-1.37m, quote.ChangePercent24h);
        Assert.Equal(31000000000.5m, quote.Volume24h);
        Assert.Equal(1850000000000m, quote.MarketCap);
        Assert.Equal(1, quote.Rank);
        Assert.Equal("CoinPaprika", result.Metadata.Source);
        Assert.Equal(new DateOnly(2026, 10, 29), result.Metadata.AsOf);
    }

    [Fact]
    public async Task Quote_supports_tiny_prices_in_exponent_notation()
    {
        const string json = """
            {"id":"shib-shiba-inu","name":"Shiba Inu","symbol":"SHIB","rank":20,
             "last_updated":"2026-10-29T10:00:00Z",
             "quotes":{"USD":{"price":1.25e-5,"volume_24h":1e8,"market_cap":7.3e9,"percent_change_24h":0}}}
            """;

        var result = await Create(new StubHandler(HttpStatusCode.OK, json))
            .GetQuoteAsync("shib-shiba-inu", "USD", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0.0000125m, result.Data.Price);
        Assert.Equal(0m, result.Data.ChangePercent24h);
    }

    [Fact]
    public async Task Quote_returns_null_for_unknown_coin()
    {
        var provider = Create(new StubHandler(HttpStatusCode.NotFound, "{\"error\":\"id not found\"}"));

        var result = await provider.GetQuoteAsync(
            "nope-nothing", "USD", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Quote_returns_null_when_requested_currency_is_missing()
    {
        // The stub answers with EUR only, so a USD request has nothing to read.
        var provider = Create(new StubHandler(HttpStatusCode.OK, TickerJson));

        var result = await provider.GetQuoteAsync(
            "btc-bitcoin", "USD", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Quote_is_cached_per_coin_and_currency()
    {
        var handler = new StubHandler(HttpStatusCode.OK, TickerJson);
        var provider = Create(handler);

        await provider.GetQuoteAsync("btc-bitcoin", "EUR", CancellationToken.None);
        await provider.GetQuoteAsync("btc-bitcoin", "EUR", CancellationToken.None);
        Assert.Equal(1, handler.Calls);

        // A different currency is a different cache entry.
        await provider.GetQuoteAsync("btc-bitcoin", "USD", CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Quote_throws_when_upstream_fails()
    {
        var provider = Create(new StubHandler(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetQuoteAsync("btc-bitcoin", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task Quote_throws_on_malformed_json()
    {
        var provider = Create(new StubHandler(HttpStatusCode.OK, "not json"));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
            provider.GetQuoteAsync("btc-bitcoin", "USD", CancellationToken.None));
    }
}
