using MarketData.Core;
using MarketData.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketData.Tests;

public sealed class ServiceRegistrationTests
{
    private static ServiceProvider BuildMarketData()
    {
        var services = new ServiceCollection();
        services.AddMarketDataProviders();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(nameof(YahooFinanceClient), "https://query1.finance.yahoo.com/", 5, "application/json", "MarketDataApi/1.0")]
    [InlineData(nameof(IEuriborRateProvider), "https://data-api.ecb.europa.eu/service/", 10, "text/csv", "")]
    [InlineData(nameof(TreasuryBondQuoteProvider), "https://home.treasury.gov/", 5, "application/xml", "")]
    [InlineData(nameof(EcbBondQuoteProvider), "https://data-api.ecb.europa.eu/service/", 10, "text/csv", "")]
    public void Typed_http_clients_are_configured_for_their_upstream(
        string clientName, string baseAddress, int timeoutSeconds, string accept, string userAgent)
    {
        using var provider = BuildMarketData();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(clientName);

        Assert.Equal(new Uri(baseAddress), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(timeoutSeconds), client.Timeout);
        Assert.Equal(accept, client.DefaultRequestHeaders.Accept.ToString());
        Assert.Equal(userAgent, client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void Market_data_interfaces_resolve_to_their_implementations()
    {
        using var provider = BuildMarketData();

        Assert.IsType<EcbEuriborRateProvider>(provider.GetRequiredService<IEuriborRateProvider>());
        Assert.IsType<YahooEquityQuoteProvider>(provider.GetRequiredService<IEquityQuoteProvider>());
        Assert.IsType<YahooCommodityQuoteProvider>(provider.GetRequiredService<ICommodityQuoteProvider>());
        Assert.IsType<CompositeBondQuoteProvider>(provider.GetRequiredService<IBondQuoteProvider>());
        Assert.IsType<YahooStockSearchProvider>(provider.GetRequiredService<IStockSearchProvider>());
    }

    [Fact]
    public void Frankfurter_base_url_defaults_when_not_configured()
    {
        var services = new ServiceCollection();
        services.AddFxProviders(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(FrankfurterFxProvider));

        Assert.Equal(new Uri("https://api.frankfurter.dev/v2/"), client.BaseAddress);
        Assert.Equal(
            "https://api.frankfurter.dev/v2/",
            provider.GetRequiredService<IOptions<FrankfurterOptions>>().Value.BaseUrl);
    }

    [Fact]
    public void Frankfurter_base_url_can_be_overridden_by_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{FrankfurterOptions.Section}:BaseUrl"] = "https://custom.test/v9/"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddFxProviders(configuration);

        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(FrankfurterFxProvider));

        Assert.Equal(new Uri("https://custom.test/v9/"), client.BaseAddress);
    }

    [Fact]
    public void Fx_provider_is_wrapped_by_the_caching_decorator()
    {
        var services = new ServiceCollection();
        services.AddFxProviders(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CachedFxRateProvider>(
            scope.ServiceProvider.GetRequiredService<IFxRateProvider>());
    }
}
