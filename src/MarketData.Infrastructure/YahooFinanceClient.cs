using MarketData.Core;
using System.Text.Json;

namespace MarketData.Infrastructure;

internal sealed record YahooMarketQuote(
    string Symbol,
    string? Name,
    string Currency,
    decimal Price,
    decimal? ChangePercent,
    long? Volume,
    DateOnly AsOf);

internal sealed class YahooFinanceClient(HttpClient http)
{
    public async Task<YahooMarketQuote?> GetQuoteAsync(
        string symbol, CancellationToken ct)
    {
        var path = $"v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=5d&interval=1d";

        using var response = await http.GetAsync(path, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(
            stream, cancellationToken: ct);

        var root = document.RootElement;

        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var result = results[0];

        if (!result.TryGetProperty("meta", out var meta))
            return null;

        var returnedSymbol = GetString(meta, "symbol") ?? symbol;
        var currency = GetString(meta, "currency");

        if (string.IsNullOrWhiteSpace(currency))
            return null;

        decimal? price = GetDecimal(meta, "regularMarketPrice");
        decimal? previous = GetDecimal(meta, "chartPreviousClose")
            ?? GetDecimal(meta, "previousClose");

        if (price is null &&
            result.TryGetProperty("indicators", out var indicators) &&
            indicators.TryGetProperty("quote", out var quotes) &&
            quotes.GetArrayLength() > 0 &&
            quotes[0].TryGetProperty("close", out var closes))
        {
            for (var i = closes.GetArrayLength() - 1; i >= 0; i--)
            {
                if (closes[i].ValueKind == JsonValueKind.Number &&
                    closes[i].TryGetDecimal(out var close))
                {
                    price = close;
                    break;
                }
            }
        }

        if (price is null)
            return null;

        decimal? changePercent = previous is > 0
            ? (price.Value - previous.Value) / previous.Value * 100m
            : GetDecimal(meta, "regularMarketChangePercent");

        long? volume = GetInt64(meta, "regularMarketVolume");
        long? timestamp = GetInt64(meta, "regularMarketTime");

        if (timestamp is null &&
            result.TryGetProperty("timestamp", out var timestamps) &&
            timestamps.ValueKind == JsonValueKind.Array &&
            timestamps.GetArrayLength() > 0 &&
            timestamps[timestamps.GetArrayLength() - 1].TryGetInt64(out var last))
        {
            timestamp = last;
        }

        if (timestamp is null)
            return null;

        var asOf = DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime);

        return new YahooMarketQuote(
            returnedSymbol,
            GetString(meta, "longName") ?? GetString(meta, "shortName"),
            currency,
            price.Value,
            changePercent,
            volume,
            asOf);
    }

    public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken ct)
    {
        var path = $"v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=100&newsCount=0";

        using var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(
            stream, cancellationToken: ct);

        var results = new List<StockSearchResult>();
        var root = document.RootElement;

        if (!root.TryGetProperty("quotes", out var quotes) ||
            quotes.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var quote in quotes.EnumerateArray())
        {
            var quoteType = GetString(quote, "quoteType");

            if (!string.Equals(
                    quoteType, "EQUITY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var symbol = GetString(quote, "symbol");
            var name = GetString(quote, "longname")
                ?? GetString(quote, "shortname");

            if (string.IsNullOrWhiteSpace(symbol) ||
                string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new StockSearchResult(
                symbol,
                name,
                GetString(quote, "exchange"),
                GetString(quote, "exchDisp")));
        }

        return results;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number
            : null;

    private static long? GetInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;
}
