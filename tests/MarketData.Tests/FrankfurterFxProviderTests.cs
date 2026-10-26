using System.Net;
using System.Text;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public class FrankfurterFxProviderTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private static FrankfurterFxProvider Create(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") });

    [Fact]
    public async Task Parses_upstream_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"amount\":1.0,\"base\":\"EUR\",\"date\":\"2026-10-23\",\"rates\":{\"USD\":1.1193}}");

        var rate = await Create(handler).GetRateAsync("EUR", "USD", CancellationToken.None);

        Assert.NotNull(rate);
        Assert.Equal(1.1193m, rate.Rate);
        Assert.Equal(new DateOnly(2026, 10, 23), rate.Date);
        Assert.Equal("Frankfurter", rate.Source);
        Assert.Equal("https://example.test/v1/latest?base=EUR&symbols=USD", handler.LastUri);
    }

    [Fact]
    public async Task Unknown_currency_returns_null()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "{\"message\":\"not found\"}");

        var rate = await Create(handler).GetRateAsync("EUR", "XXX", CancellationToken.None);

        Assert.Null(rate);
    }

    [Fact]
    public async Task Invalid_json_response_throws_instead_of_returning_null()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{ invalid json");

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            Create(handler).GetRateAsync("EUR", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task Upstream_server_error_is_not_swallowed()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(handler).GetRateAsync("EUR", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task HttpClient_timeout_throws_cancellation_exception()
    {
        using var http = new HttpClient(new TimeoutHandler())
        {
            BaseAddress = new Uri("https://example.test/v1/"),
            Timeout = TimeSpan.FromMilliseconds(100)
        };

        var provider = new FrankfurterFxProvider(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRateAsync("EUR", "USD", CancellationToken.None));
    }
}
