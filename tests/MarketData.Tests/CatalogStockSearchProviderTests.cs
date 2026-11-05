using System.Text;
using MarketData.Infrastructure;

namespace MarketData.Tests;

public sealed class CatalogStockSearchProviderTests : IDisposable
{
    private const string Header = "ticker,name,exchange,asset_type\n";

    private readonly string _dir =
        Directory.CreateTempSubdirectory("catalog-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private CatalogStockSearchProvider CreateFrom(string csv)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv);
        return new CatalogStockSearchProvider(path);
    }

    private static async Task<IReadOnlyList<MarketData.Core.StockSearchResult>> Search(
        CatalogStockSearchProvider provider, string query) =>
        await provider.SearchAsync(query, CancellationToken.None);

    [Fact]
    public async Task Matches_by_symbol_or_name_ignoring_case_and_excludes_non_stocks()
    {
        var provider = CreateFrom(
            Header +
            "AAPL,Apple Inc,NASDAQ,Stock\n" +
            "MSFT,Microsoft Corp,NASDAQ,Stock\n" +
            "SPY,SPDR S&P 500,NYSE,ETF\n");

        Assert.Equal("AAPL", Assert.Single(await Search(provider, "aapl")).Symbol);
        Assert.Equal("MSFT", Assert.Single(await Search(provider, "MICROSOFT")).Symbol);
        Assert.Empty(await Search(provider, "spy"));
    }

    [Fact]
    public async Task Queries_shorter_than_two_characters_return_nothing_without_loading_the_file()
    {
        var provider = new CatalogStockSearchProvider(Path.Combine(_dir, "missing.csv"));

        Assert.Empty(await Search(provider, "a"));
        Assert.Empty(await Search(provider, "  a  "));
        Assert.Empty(await Search(provider, ""));
    }

    [Fact]
    public async Task Puts_names_that_start_with_the_query_first_then_sorts_by_name()
    {
        var provider = CreateFrom(
            Header +
            "ZAC,Zeta Acme Corp,NYSE,Stock\n" +
            "AH,Acme Holdings,NYSE,Stock\n" +
            "BA,Beta Acme,NYSE,Stock\n");

        var results = await Search(provider, "acme");

        Assert.Equal(new[] { "AH", "BA", "ZAC" }, results.Select(r => r.Symbol));
    }

    [Fact]
    public async Task Returns_at_most_one_hundred_results()
    {
        var csv = new StringBuilder(Header);
        for (var i = 0; i < 150; i++)
            csv.Append($"T{i:D3},Acme {i:D3} Corp,NYSE,Stock\n");

        var results = await Search(CreateFrom(csv.ToString()), "acme");

        Assert.Equal(100, results.Count);
    }

    [Fact]
    public async Task Cleans_the_catalog_while_loading_it()
    {
        var provider = CreateFrom(
            "Ticker, Name ,Exchange,Asset_Type\n" +          // headers are trimmed and case-insensitive
            "AAA,\"Alpha, Beta & Co\",NYSE,Stock\n" +        // quoted field containing a comma
            "BBB,Bravo Inc,,stock\n" +                       // empty exchange becomes null
            ",No Symbol,NYSE,Stock\n" +                      // skipped: no symbol
            "CCC,,NYSE,Stock\n" +                            // skipped: no name
            "DDD,Delta Fund,NYSE,ETF\n" +                    // skipped: not a stock
            "EEE,Echo\n" +                                   // skipped: too few columns
            "AAA,\"Alpha, Beta & Co\",NYSE,Stock\n" +        // skipped: duplicate symbol+exchange
            "AAA,Alpha Other Exchange,LSE,Stock\n");         // kept: same symbol, other exchange

        var beta = Assert.Single(await Search(provider, "beta"));
        Assert.Equal("AAA", beta.Symbol);
        Assert.Equal("Alpha, Beta & Co", beta.Name);
        Assert.Equal("NYSE", beta.Exchange);
        Assert.Equal("NYSE", beta.ExchangeName);

        var alpha = await Search(provider, "alpha");
        Assert.Equal(new[] { "LSE", "NYSE" }, alpha.Select(r => r.Exchange).Order());

        var bravo = Assert.Single(await Search(provider, "bravo"));
        Assert.Null(bravo.Exchange);
        Assert.Null(bravo.ExchangeName);

        Assert.Empty(await Search(provider, "no symbol"));
        Assert.Empty(await Search(provider, "ccc"));
        Assert.Empty(await Search(provider, "delta"));
        Assert.Empty(await Search(provider, "echo"));
    }

    [Fact]
    public async Task A_missing_file_throws_FileNotFoundException()
    {
        var provider = new CatalogStockSearchProvider(Path.Combine(_dir, "missing.csv"));

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            provider.SearchAsync("ab", CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_file_throws_InvalidDataException()
    {
        var provider = CreateFrom("");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.SearchAsync("ab", CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_required_column_throws_InvalidDataException()
    {
        var provider = CreateFrom("ticker,name,exchange\nAAA,Alpha,NYSE\n");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.SearchAsync("ab", CancellationToken.None));

        Assert.Contains("asset_type", ex.Message);
    }

    [Fact]
    public async Task A_cancelled_token_is_honoured_before_any_work()
    {
        var provider = CreateFrom(Header);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.SearchAsync("ab", cts.Token));
    }
}
