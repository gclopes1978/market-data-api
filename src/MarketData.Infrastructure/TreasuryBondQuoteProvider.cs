using System.Globalization;
using System.Xml.Linq;
using MarketData.Core;

namespace MarketData.Infrastructure;

internal sealed class TreasuryBondQuoteProvider(HttpClient http)
    : IBondQuoteProvider
{
    private static readonly Dictionary<string, string> YieldFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["US1M"] = "BC_1MONTH",
            ["US3M"] = "BC_3MONTH",
            ["US6M"] = "BC_6MONTH",
            ["US1Y"] = "BC_1YEAR",
            ["US2Y"] = "BC_2YEAR",
            ["US5Y"] = "BC_5YEAR",
            ["US7Y"] = "BC_7YEAR",
            ["US10Y"] = "BC_10YEAR",
            ["US20Y"] = "BC_20YEAR",
            ["US30Y"] = "BC_30YEAR"
        };

    public async Task<MarketDataEnvelope<BondQuote>?> GetQuoteAsync(
        string market, string identifier, CancellationToken ct)
    {
        if (!market.Equals("US", StringComparison.OrdinalIgnoreCase) ||
            !YieldFields.TryGetValue(identifier, out var yieldField))
        {
            return null;
        }

        for (var monthsBack = 0; monthsBack < 3; monthsBack++)
        {
            var month = DateTime.UtcNow.AddMonths(-monthsBack)
                .ToString("yyyyMM", CultureInfo.InvariantCulture);

            var path =
                "resource-center/data-chart-center/interest-rates/pages/xml" +
                $"?data=daily_treasury_yield_curve&field_tdr_date_value_month={month}";

            using var response = await http.GetAsync(path, ct);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(ct);
            var document = XDocument.Parse(xml);

            var rows = document.Descendants()
                .Where(element => element.Name.LocalName == "entry");

            foreach (var row in rows)
            {
                var dateText = row.Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName == "NEW_DATE")?.Value;

                var yieldText = row.Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName == yieldField)?.Value;

                if (!DateTime.TryParse(
                        dateText,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var date))
                {
                    continue;
                }

                if (!decimal.TryParse(
                        yieldText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var yield))
                {
                    continue;
                }

                var asOf = DateOnly.FromDateTime(date);

                return new MarketDataEnvelope<BondQuote>(
                    new BondQuote(
                        identifier.ToUpperInvariant(),
                        "US",
                        null,
                        "USD",
                        yield,
                        null),
                    new MarketDataMetadata(
                        "U.S. Treasury Daily Treasury Par Yield Curve",
                        asOf));
            }
        }

        return null;
    }
}
