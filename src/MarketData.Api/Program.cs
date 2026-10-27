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

app.Run();

// Required by WebApplicationFactory<Program> in the test project
public partial class Program { }