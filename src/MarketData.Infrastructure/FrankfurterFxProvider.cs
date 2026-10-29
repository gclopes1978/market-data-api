using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MarketData.Core;

namespace MarketData.Infrastructure;

public sealed class FrankfurterFxProvider(HttpClient http) : IFxRateProvider
{
    private sealed record Response(
        [property: JsonPropertyName("date")] DateOnly Date,
        [property: JsonPropertyName("base")] string Base,
        [property: JsonPropertyName("quote")] string Quote,
        [property: JsonPropertyName("rate")] decimal Rate);

    public async Task<FxRate?> GetRateAsync(
        string baseCcy,
        string quoteCcy,
        CancellationToken ct)
    {
        var path = $"rate/{Uri.EscapeDataString(baseCcy)}/{Uri.EscapeDataString(quoteCcy)}";

        using var resp = await http.GetAsync(path, ct);

        // Unknown currency or currency pair: treat as not found.
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            return null;

        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<Response>(
            cancellationToken: ct);

        return dto is not null
            ? new FxRate(baseCcy, quoteCcy, dto.Rate, dto.Date, "Frankfurter")
            : null;
    }
}
