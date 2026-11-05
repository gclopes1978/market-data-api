using MarketData.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MarketData.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Frankfurter FX provider (typed HTTP client behind a circuit breaker) wrapped by a one-hour cache.</summary>
    public static IServiceCollection AddFxProviders(
        this IServiceCollection services, IConfiguration config)
    {
        var breaker = ReadBreakerSettings(config);

        services.AddMemoryCache();
        services.AddOptions<FrankfurterOptions>()
            .Bind(config.GetSection(FrankfurterOptions.Section));

        services.AddHttpClient<FrankfurterFxProvider>((sp, http) =>
        {
            http.BaseAddress = new Uri(
                sp.GetRequiredService<IOptions<FrankfurterOptions>>()
                    .Value.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddUpstreamCircuitBreaker("frankfurter", breaker, TimeSpan.FromSeconds(5));

        services.AddScoped<IFxRateProvider>(sp => new CachedFxRateProvider(
            sp.GetRequiredService<FrankfurterFxProvider>(),
            sp.GetRequiredService<IMemoryCache>()));

        return services;
    }

    /// <summary>
    /// Registers every market data provider (Euribor, bonds, stocks/ETFs, commodities, crypto) with a typed HTTP client
    /// per upstream, each one protected by its own circuit breaker.
    /// </summary>
    public static IServiceCollection AddMarketDataProviders(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        var breaker = ReadBreakerSettings(configuration);

        services.AddHttpClient<IEuriborRateProvider, EcbEuriborRateProvider>(
        http =>
        {
            http.BaseAddress = new Uri(
                "https://data-api.ecb.europa.eu/service/");
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.Accept.ParseAdd("text/csv");
        })
        .AddUpstreamCircuitBreaker("ecb-euribor", breaker, TimeSpan.FromSeconds(10));

        services.AddHttpClient<YahooFinanceClient>(http =>
        {
            http.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
            http.Timeout = TimeSpan.FromSeconds(5);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MarketDataApi/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .AddUpstreamCircuitBreaker("yahoo-finance", breaker, TimeSpan.FromSeconds(5));

        services.AddTransient<IEquityQuoteProvider, YahooEquityQuoteProvider>();
        services.AddTransient<ICommodityQuoteProvider, YahooCommodityQuoteProvider>();

        // One typed HTTP client per bond source; the composite below routes each market to its source.
        services.AddHttpClient<TreasuryBondQuoteProvider>(
            http =>
            {
                http.BaseAddress = new Uri("https://home.treasury.gov/");
                http.Timeout = TimeSpan.FromSeconds(5);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
            })
            .AddUpstreamCircuitBreaker("us-treasury", breaker, TimeSpan.FromSeconds(5));

        services.AddHttpClient<EcbBondQuoteProvider>(
            http =>
            {
                http.BaseAddress = new Uri(
                    "https://data-api.ecb.europa.eu/service/");
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.Accept.ParseAdd("text/csv");
            })
            .AddUpstreamCircuitBreaker("ecb-bonds", breaker, TimeSpan.FromSeconds(10));

        services.AddTransient<IBondQuoteProvider, CompositeBondQuoteProvider>();

        services.AddTransient<IStockSearchProvider, YahooStockSearchProvider>();

        // Crypto: one typed client serves both the catalog and the quotes (CoinPaprika, no API key).
        services.AddMemoryCache();
        services.AddHttpClient<CoinPaprikaCryptoProvider>(http =>
        {
            http.BaseAddress = new Uri("https://api.coinpaprika.com/v1/");
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MarketDataApi/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .AddUpstreamCircuitBreaker("coinpaprika", breaker, TimeSpan.FromSeconds(15));
        services.AddTransient<ICryptoCatalogProvider>(sp =>
            sp.GetRequiredService<CoinPaprikaCryptoProvider>());
        services.AddTransient<ICryptoQuoteProvider>(sp =>
            sp.GetRequiredService<CoinPaprikaCryptoProvider>());

        return services;
    }

    private static CircuitBreakerSettings ReadBreakerSettings(IConfiguration? configuration)
    {
        var settings = new CircuitBreakerSettings();
        configuration?.GetSection(CircuitBreakerSettings.Section).Bind(settings);
        return settings;
    }
}
