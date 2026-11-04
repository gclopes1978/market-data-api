using MarketData.Api;
using MarketData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddFxProviders(builder.Configuration);
builder.Services.AddMarketDataProviders();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapFx();
app.MapMarketData();

await app.RunAsync();
