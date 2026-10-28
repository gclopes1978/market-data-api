using System.Globalization;
using System.Text;
using MarketData.Core;

namespace MarketData.Infrastructure;

/// <summary>
/// Euro area government bond yields from the ECB Data Portal (dataset "YC").
///
/// Important: this is NOT the price of one specific bond. The ECB publishes a daily *yield curve*
/// fitted (Svensson model) on euro area government bonds rated AAA. Each identifier (EUR10Y...) is one
/// point of that curve: the spot rate for that maturity, in percent per annum.
/// Why the ECB: it is free, needs no API key, and the API already uses the same portal for Euribor.
/// </summary>
public sealed class EcbBondQuoteProvider(HttpClient http) : IBondQuoteProvider
{
    private const string Market = "EUR";

    // Public identifier -> ECB series suffix. Only maturities that exist in the ECB dataset are listed.
    private static readonly IReadOnlyDictionary<string, string> Maturities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR3M"] = "SR_3M",
            ["EUR6M"] = "SR_6M",
            ["EUR1Y"] = "SR_1Y",
            ["EUR2Y"] = "SR_2Y",
            ["EUR5Y"] = "SR_5Y",
            ["EUR7Y"] = "SR_7Y",
            ["EUR10Y"] = "SR_10Y",
            ["EUR20Y"] = "SR_20Y",
            ["EUR30Y"] = "SR_30Y"
        };

    public async Task<MarketDataEnvelope<BondQuote>?> GetQuoteAsync(
        string market, string identifier, CancellationToken ct)
    {
        // Not our market or maturity: answer "not found" without calling the ECB.
        if (!market.Equals(Market, StringComparison.OrdinalIgnoreCase) ||
            !Maturities.TryGetValue(identifier, out var series))
        {
            return null;
        }

        // Series key = frequency.area.currency.provider.instrument.model.datatype.
        // lastNObservations=1 asks only for the most recent business day.
        var url =
            $"data/YC/B.U2.EUR.4F.G_N_A.SV_C_YM.{series}" +
            "?format=csvdata&lastNObservations=1";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync(ct);
        var observation = ParseLatestObservation(csv);

        if (observation is null)
        {
            return null;
        }

        var (date, yieldPercent) = observation.Value;

        // Price and maturity date stay null: a fitted curve has no price or single maturity date.
        return new MarketDataEnvelope<BondQuote>(
            new BondQuote(
                identifier.ToUpperInvariant(),
                Market,
                null,
                "EUR",
                yieldPercent,
                null),
            new MarketDataMetadata(
                "ECB Data Portal (euro area AAA government bond yield curve, spot rate)",
                date));
    }

    // Reads the last data row of the ECB CSV: TIME_PERIOD (yyyy-MM-dd) and OBS_VALUE columns.
    private static (DateOnly Date, decimal Yield)? ParseLatestObservation(string csv)
    {
        using var reader = new StringReader(csv);
        var headerLine = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return null;
        }

        var headers = ParseCsvLine(headerLine.TrimStart('\uFEFF'));
        var dateIndex = headers.FindIndex(
            h => string.Equals(h, "TIME_PERIOD", StringComparison.OrdinalIgnoreCase));
        var valueIndex = headers.FindIndex(
            h => string.Equals(h, "OBS_VALUE", StringComparison.OrdinalIgnoreCase));

        if (dateIndex < 0 || valueIndex < 0)
        {
            throw new FormatException(
                "The ECB response did not contain TIME_PERIOD and OBS_VALUE columns.");
        }

        (DateOnly Date, decimal Yield)? latest = null;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count <= Math.Max(dateIndex, valueIndex))
            {
                continue;
            }

            if (!DateOnly.TryParseExact(
                    fields[dateIndex],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date) ||
                !decimal.TryParse(
                    fields[valueIndex],
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var yieldPercent))
            {
                throw new FormatException(
                    "The ECB response contained an invalid observation.");
            }

            latest = (date, yieldPercent);
        }

        return latest;
    }

    // Minimal CSV line parser that understands quoted fields (the ECB puts commas inside titles).
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
