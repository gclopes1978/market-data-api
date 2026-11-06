using MarketData.Api;
using MarketData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddFxProviders(builder.Configuration);
builder.Services.AddMarketDataProviders(builder.Configuration);

var app = builder.Build();

// Order matters: the exception handler wraps everything, the limiter rejects before any endpoint runs.
app.UseExceptionHandling();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapFx();
app.MapMarketData();

await app.RunAsync();
