using MarketData.Core;
using Microsoft.VisualBasic.FileIO;

namespace MarketData.Infrastructure;

internal sealed class CatalogStockSearchProvider : IStockSearchProvider
{
    private readonly Lazy<IReadOnlyList<StockSearchResult>> _stocks =
        new(LoadStocks);

    public Task<IReadOnlyList<StockSearchResult>> SearchAsync(
        string query,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var term = query.Trim();

        if (term.Length < 2)
        {
            return Task.FromResult<IReadOnlyList<StockSearchResult>>(
                Array.Empty<StockSearchResult>());
        }

        var normalized = term.ToUpperInvariant();

        var results = _stocks.Value
            .Where(stock =>
                stock.Symbol.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                stock.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(stock =>
                stock.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(stock => stock.Name)
            .Take(100)
            .ToArray();

        return Task.FromResult<IReadOnlyList<StockSearchResult>>(results);
    }

    private static IReadOnlyList<StockSearchResult> LoadStocks()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Data", "tickers.csv");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"O catálogo de ações não foi encontrado: {path}", path);
        }

        var stocks = new List<StockSearchResult>();

        using var parser = new TextFieldParser(path);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        var headers = parser.ReadFields()
            ?? throw new InvalidDataException("O catálogo CSV está vazio.");

        var columns = headers
            .Select((header, index) => new { header, index })
            .ToDictionary(x => x.header.Trim(), x => x.index,
                StringComparer.OrdinalIgnoreCase);

        int IndexOf(string name) =>
            columns.TryGetValue(name, out var index)
                ? index
                : throw new InvalidDataException(
                    $"Falta a coluna '{name}' no catálogo.");

        var tickerIndex = IndexOf("ticker");
        var nameIndex = IndexOf("name");
        var exchangeIndex = IndexOf("exchange");
        var typeIndex = IndexOf("asset_type");

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null ||
                fields.Length <= Math.Max(
                    Math.Max(tickerIndex, nameIndex),
                    Math.Max(exchangeIndex, typeIndex)))
            {
                continue;
            }

            if (!string.Equals(
                    fields[typeIndex].Trim(), "Stock",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var symbol = fields[tickerIndex].Trim();
            var name = fields[nameIndex].Trim();
            var exchange = fields[exchangeIndex].Trim();

            if (symbol.Length == 0 || name.Length == 0)
                continue;

            stocks.Add(new StockSearchResult(
                symbol,
                name,
                exchange.Length == 0 ? null : exchange,
                exchange.Length == 0 ? null : exchange));
        }

        return [.. stocks.DistinctBy(stock => $"{stock.Symbol}|{stock.Exchange}")];
    }
}