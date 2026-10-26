namespace MarketData.Core;

public sealed record FxRate(string Base, string Quote, decimal Rate, DateOnly Date, string Source);