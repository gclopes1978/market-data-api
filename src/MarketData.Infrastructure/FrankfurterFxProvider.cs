using System.Net;
using System.Net.Http.Json;
using MarketData.Core;

namespace MarketData.Infrastructure;

public sealed class FrankfurterFxProvider(HttpClient http) : IFxRateProvider
{
    private sealed record Response(DateOnly Date, Dictionary<string, decimal> Rates);

    public async Task<FxRate?> GetRateAsync(string baseCcy, string quoteCcy, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"latest?base={baseCcy}&symbols={quoteCcy}", ct);

        // Unknown currency: treat as "not found" instead of a server error
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            return null;

        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<Response>(ct);

        return dto is not null && dto.Rates.TryGetValue(quoteCcy, out var rate)
            ? new FxRate(baseCcy, quoteCcy, rate, dto.Date, "Frankfurter")
            : null;
    }
}