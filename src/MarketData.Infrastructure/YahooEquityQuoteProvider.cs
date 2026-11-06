using MarketData.Core;

namespace MarketData.Infrastructure;

internal sealed class YahooEquityQuoteProvider(
    YahooFinanceClient yahoo) : IEquityQuoteProvider
{
    /// <summary>Maps a Yahoo Finance chart quote to an <see cref="EquityQuote"/>; returns <c>null</c> when Yahoo does not know the ticker.</summary>
    public async Task<MarketDataEnvelope<EquityQuote>?> GetQuoteAsync(
        string ticker, CancellationToken ct)
    {
        var quote = await yahoo.GetQuoteAsync(ticker, ct);

        if (quote is null)
            return null;

        return new MarketDataEnvelope<EquityQuote>(
            new EquityQuote(
                ticker.ToUpperInvariant(),
                quote.Name,
                quote.Price,
                quote.Currency,
                quote.ChangePercent,
                quote.Volume),
            new MarketDataMetadata(
                "Yahoo Finance (unofficial chart endpoint)",
                quote.AsOf));
    }
}
