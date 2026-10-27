using MarketData.Core;

namespace MarketData.Infrastructure;

internal sealed class YahooCommodityQuoteProvider(
    YahooFinanceClient yahoo) : ICommodityQuoteProvider
{
    private sealed record Instrument(
        string YahooSymbol, string Name, string Unit);

    private static readonly Dictionary<string, Instrument> Instruments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GOLD"] = new("GC=F", "Gold futures", "USD per troy ounce"),
            ["XAU"] = new("GC=F", "Gold futures", "USD per troy ounce"),
            ["SILVER"] = new("SI=F", "Silver futures", "USD per troy ounce"),
            ["XAG"] = new("SI=F", "Silver futures", "USD per troy ounce"),
            ["WTI"] = new("CL=F", "WTI crude oil futures", "USD per barrel"),
            ["BRENT"] = new("BZ=F", "Brent crude oil futures", "USD per barrel"),
            ["NATGAS"] = new("NG=F", "Natural gas futures", "USD per MMBtu")
        };

    public async Task<MarketDataEnvelope<CommodityQuote>?> GetQuoteAsync(
        string symbol, CancellationToken ct)
    {
        if (!Instruments.TryGetValue(symbol, out var instrument))
            return null;

        var quote = await yahoo.GetQuoteAsync(instrument.YahooSymbol, ct);

        if (quote is null)
            return null;

        return new MarketDataEnvelope<CommodityQuote>(
            new CommodityQuote(
                symbol.ToUpperInvariant(),
                instrument.Name,
                quote.Price,
                quote.Currency,
                instrument.Unit),
            new MarketDataMetadata(
                "Yahoo Finance (unofficial chart endpoint)",
                quote.AsOf));
    }
}
