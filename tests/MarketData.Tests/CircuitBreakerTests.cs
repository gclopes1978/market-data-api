using System.Net;
using MarketData.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace MarketData.Tests;

public sealed class CircuitBreakerTests
{
    private const string Url = "https://upstream.test/resource";

    private sealed class SlowHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static CircuitBreakerSettings Settings(bool enabled = true) => new()
    {
        Enabled = enabled,
        FailureRatio = 0.5,
        MinimumThroughput = 2,
        SamplingDurationSeconds = 30,
        BreakDurationSeconds = 30
    };

    private static HttpClient CreateClient(
        HttpMessageHandler handler, CircuitBreakerSettings settings, TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("upstream")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddUpstreamCircuitBreaker("upstream", settings, timeout ?? TimeSpan.FromSeconds(5));

        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("upstream");
    }

    [Fact]
    public async Task Opens_after_repeated_server_errors_and_then_fails_fast()
    {
        var handler = FakeHttpHandler.Always(HttpStatusCode.InternalServerError, "{}");
        var client = CreateClient(handler, Settings());

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync(Url)).StatusCode);

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(Url));
        Assert.Equal(2, handler.Calls); // the third call never reached the upstream
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Throttling_and_gateway_errors_count_as_failures(HttpStatusCode status)
    {
        var handler = FakeHttpHandler.Always(status, "{}");
        var client = CreateClient(handler, Settings());

        for (var i = 0; i < 2; i++)
            await client.GetAsync(Url);

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(Url));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Successes_and_client_errors_never_open_the_circuit(HttpStatusCode status)
    {
        var handler = FakeHttpHandler.Always(status, "{}");
        var client = CreateClient(handler, Settings());

        for (var i = 0; i < 10; i++)
            Assert.Equal(status, (await client.GetAsync(Url)).StatusCode);

        Assert.Equal(10, handler.Calls);
    }

    [Fact]
    public async Task Transport_errors_count_as_failures()
    {
        var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler, Settings());

        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Url));

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(Url));
    }

    [Fact]
    public async Task Hanging_upstream_times_out_inside_the_pipeline_and_opens_the_circuit()
    {
        var handler = new SlowHandler();
        var client = CreateClient(handler, Settings(), TimeSpan.FromSeconds(1)); // attempt timeout = 500 ms

        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.GetAsync(Url));

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(Url));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        var handler = FakeHttpHandler.Always(HttpStatusCode.InternalServerError, "{}");
        var client = CreateClient(handler, Settings(enabled: false));

        for (var i = 0; i < 10; i++)
            await client.GetAsync(Url);

        Assert.Equal(10, handler.Calls);
    }

    [Theory]
    [InlineData(0.0, 5, 30, 30)]
    [InlineData(1.5, 5, 30, 30)]
    [InlineData(0.5, 1, 30, 30)]
    [InlineData(0.5, 5, 0, 30)]
    [InlineData(0.5, 5, 30, 0)]
    public void Validate_rejects_out_of_range_values(
        double ratio, int throughput, int sampling, int breakDuration)
    {
        var settings = new CircuitBreakerSettings
        {
            FailureRatio = ratio,
            MinimumThroughput = throughput,
            SamplingDurationSeconds = sampling,
            BreakDurationSeconds = breakDuration
        };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    [Fact]
    public void Provider_registration_binds_settings_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:CircuitBreaker:MinimumThroughput"] = "1" // invalid → proves the section is read
            })
            .Build();

        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddMarketDataProviders(configuration));
    }
}
