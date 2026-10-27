using System.Globalization;
using System.Text;
using MarketData.Core;

namespace MarketData.Infrastructure;

public sealed class EcbEuriborRateProvider(HttpClient http)
    : IEuriborRateProvider
{
    private static readonly IReadOnlyDictionary<string, string> SeriesKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1M"] = "EURIBOR1MD_",
            ["3M"] = "EURIBOR3MD_",
            ["6M"] = "EURIBOR6MD_",
            ["12M"] = "EURIBOR1YD_"
        };

    public async Task<MarketDataEnvelope<EuriborRate>?> GetRateAsync(
        string tenor, CancellationToken ct)
    {
        var normalizedTenor = tenor.ToUpperInvariant();

        if (!SeriesKeys.TryGetValue(normalizedTenor, out var series))
        {
            throw new ArgumentException("Unsupported Euribor tenor.", nameof(tenor));
        }

        var url =
            $"data/FM/M.U2.EUR.RT.MM.{series}.HSTA" +
            "?format=csvdata&lastNObservations=1";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync(ct);
        var observation = ParseLatestObservation(csv);

        if (observation is null)
        {
            return null;
        }

        var (period, rate) = observation.Value;

        return new MarketDataEnvelope<EuriborRate>(
            new EuriborRate(
                normalizedTenor,
                rate,
                "Percent per annum",
                "Monthly average"),
            new MarketDataMetadata(
                "ECB Data Portal (financial market data)",
                period));
    }

    private static (DateOnly Period, decimal Rate)? ParseLatestObservation(
        string csv)
    {
        using var reader = new StringReader(csv);
        var headerLine = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return null;
        }

        var headers = ParseCsvLine(headerLine.TrimStart('\uFEFF'));
        var periodIndex = headers.FindIndex(
            h => string.Equals(h, "TIME_PERIOD",
                StringComparison.OrdinalIgnoreCase));
        var valueIndex = headers.FindIndex(
            h => string.Equals(h, "OBS_VALUE",
                StringComparison.OrdinalIgnoreCase));

        if (periodIndex < 0 || valueIndex < 0)
        {
            throw new FormatException(
                "The ECB response did not contain TIME_PERIOD and OBS_VALUE columns.");
        }

        (DateOnly Period, decimal Rate)? latest = null;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count <= Math.Max(periodIndex, valueIndex))
            {
                continue;
            }

            if (!DateOnly.TryParseExact(
                    fields[periodIndex],
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var period) ||
                !decimal.TryParse(
                    fields[valueIndex],
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var rate))
            {
                throw new FormatException(
                    "The ECB response contained an invalid observation.");
            }

            latest = (period, rate);
        }

        return latest;
    }

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