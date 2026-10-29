namespace MarketData.Infrastructure;

public sealed class FrankfurterOptions
{
    public const string Section = "Providers:Frankfurter";

    public string BaseUrl { get; set; } = "https://api.frankfurter.dev/v2/";
}
