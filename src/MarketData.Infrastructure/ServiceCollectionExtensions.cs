using MarketData.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketData.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFxProviders(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddOptions<FrankfurterOptions>().Bind(config.GetSection(FrankfurterOptions.Section));

        // The concrete provider gets a typed HttpClient; consumers only see IFxRateProvider
        services.AddHttpClient<FrankfurterFxProvider>((sp, http) =>
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<FrankfurterOptions>>().Value.BaseUrl));

        // Composition of the decorator chain lives here, not in the host
        services.AddScoped<IFxRateProvider>(sp => new CachedFxRateProvider(
            sp.GetRequiredService<FrankfurterFxProvider>(),
            sp.GetRequiredService<IMemoryCache>()));

        return services;
    }
}