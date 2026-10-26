using MarketData.Core;
using MarketData.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace MarketData.Tests;

public class CachedFxRateProviderTests
{
    [Fact]
    public async Task Second_call_for_same_pair_is_served_from_cache()
    {
        var inner = new FakeFxProvider
        {
            Result = new FxRate("EUR", "USD", 1.1m, new DateOnly(2026, 10, 23), "Test")
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new CachedFxRateProvider(inner, cache);

        await sut.GetRateAsync("EUR", "USD", CancellationToken.None);
        await sut.GetRateAsync("EUR", "USD", CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Different_pairs_do_not_share_cache_entries()
    {
        var inner = new FakeFxProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new CachedFxRateProvider(inner, cache);

        await sut.GetRateAsync("EUR", "USD", CancellationToken.None);
        await sut.GetRateAsync("EUR", "GBP", CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }
}