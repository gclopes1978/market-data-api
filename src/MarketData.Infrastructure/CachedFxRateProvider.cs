using MarketData.Core;
using Microsoft.Extensions.Caching.Memory;

namespace MarketData.Infrastructure;

// Decorator: keeps upstream calls within provider quotas
public sealed class CachedFxRateProvider(IFxRateProvider inner, IMemoryCache cache) : IFxRateProvider
{
    public Task<FxRate?> GetRateAsync(string baseCcy, string quoteCcy, CancellationToken ct) =>
        cache.GetOrCreateAsync($"fx:{baseCcy}:{quoteCcy}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return inner.GetRateAsync(baseCcy, quoteCcy, ct);
        });
}