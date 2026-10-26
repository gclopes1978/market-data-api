using MarketData.Core;

namespace MarketData.Tests;

public sealed class FakeFxProvider : IFxRateProvider
{
    public FxRate? Result { get; set; }
    public int Calls { get; private set; }
    public string? LastBase { get; private set; }
    public string? LastQuote { get; private set; }

    public Task<FxRate?> GetRateAsync(string baseCcy, string quoteCcy, CancellationToken ct)
    {
        Calls++;
        LastBase = baseCcy;
        LastQuote = quoteCcy;
        return Task.FromResult(Result);
    }

    public void Reset()
    {
        Result = null;
        Calls = 0;
        LastBase = null;
        LastQuote = null;
    }
}