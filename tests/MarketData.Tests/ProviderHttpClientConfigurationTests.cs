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
}
