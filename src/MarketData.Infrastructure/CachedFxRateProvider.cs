using MarketData.Core;
using Microsoft.Extensions.Caching.Memory;

namespace MarketData.Infrastructure;

// Decorator: keeps upstream calls within provider quotas
public sealed class CachedFxRateProvider(IFxRateProvider inner, IMemoryCache cache) : IFxRateProvider
{
    /// <summary>Returns the rate from the cache, calling the inner provider (and caching the result for one hour) on a miss.</summary>
    public Task<FxRate?> GetRateAsync(string baseCcy, string quoteCcy, CancellationToken ct) =>
        cache.GetOrCreateAsync($"fx:{baseCcy}:{quoteCcy}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return inner.GetRateAsync(baseCcy, quoteCcy, ct);
        });
}