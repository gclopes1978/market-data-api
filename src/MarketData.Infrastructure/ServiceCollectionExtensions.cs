using MarketData.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketData.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFxProviders(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddOptions<FrankfurterOptions>()
            .Bind(config.GetSection(FrankfurterOptions.Section));

        services.AddHttpClient<FrankfurterFxProvider>((sp, http) =>
        {
            http.BaseAddress = new Uri(
                sp.GetRequiredService<IOptions<FrankfurterOptions>>()
                    .Value.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddScoped<IFxRateProvider>(sp => new CachedFxRateProvider(
            sp.GetRequiredService<FrankfurterFxProvider>(),
            sp.GetRequiredService<IMemoryCache>()));

        return services;
    }

    public static IServiceCollection AddMarketDataProviders(
        this IServiceCollection services)
    {
        services.AddHttpClient<IEuriborRateProvider, EcbEuriborRateProvider>(
        http =>
        {
            http.BaseAddress = new Uri(
                "https://data-api.ecb.europa.eu/service/");
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.Accept.ParseAdd("text/csv");
        });

        services.AddHttpClient<YahooFinanceClient>(http =>
        {
            http.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
            http.Timeout = TimeSpan.FromSeconds(5);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MarketDataApi/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddTransient<IEquityQuoteProvider, YahooEquityQuoteProvider>();
        services.AddTransient<ICommodityQuoteProvider, YahooCommodityQuoteProvider>();

        // One typed HTTP client per bond source; the composite below routes each market to its source.
        services.AddHttpClient<TreasuryBondQuoteProvider>(
            http =>
            {
                http.BaseAddress = new Uri("https://home.treasury.gov/");
                http.Timeout = TimeSpan.FromSeconds(5);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
            });

        services.AddHttpClient<EcbBondQuoteProvider>(
            http =>
            {
                http.BaseAddress = new Uri(
                    "https://data-api.ecb.europa.eu/service/");
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.Accept.ParseAdd("text/csv");
            });

        services.AddTransient<IBondQuoteProvider, CompositeBondQuoteProvider>();

        return services;
    }
}
