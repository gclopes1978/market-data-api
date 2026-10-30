using MarketData.Core;
using MarketData.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketData.Tests;

public sealed class ProviderHttpClientConfigurationTests
{
    [Fact]
    public void Typed_http_client_uses_five_second_timeout()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddFxProviders(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(nameof(FrankfurterFxProvider));

        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
    }

    [Fact]
    public void Crypto_http_client_uses_fifteen_second_timeout_and_coinpaprika_base_address()
    {
        var services = new ServiceCollection();
        services.AddMarketDataProviders();

        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(nameof(CoinPaprikaCryptoProvider));

        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
        Assert.Equal(new Uri("https://api.coinpaprika.com/v1/"), client.BaseAddress);
    }

    [Fact]
    public void Crypto_interfaces_resolve_to_the_coinpaprika_provider()
    {
        var services = new ServiceCollection();
        services.AddMarketDataProviders();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<CoinPaprikaCryptoProvider>(
            serviceProvider.GetRequiredService<ICryptoCatalogProvider>());
        Assert.IsType<CoinPaprikaCryptoProvider>(
            serviceProvider.GetRequiredService<ICryptoQuoteProvider>());
    }
}
