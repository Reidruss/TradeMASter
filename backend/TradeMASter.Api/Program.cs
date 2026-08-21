using Scalar.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using TradeMASter.Agents;
using TradeMASter.Api.Endpoints;
using TradeMASter.Api.Hubs;
using TradeMASter.Api.Services;
using TradeMASter.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .SetApplicationName("TradeMASter");

// Configure Dependency Injection
builder.Services.AddSingleton<ITodoService, InMemoryTodoService>();

// Configure Infrastructure (Database, EF Core, Cache, Market Data, Paper Broker, Robinhood)
builder.Services.AddInfrastructure(builder.Configuration);

// Configure Multi-Agent Committee Intelligence Tier, Backtesting Engine & Bi-Weekly Optimizer
builder.Services.AddAgentCommittee(builder.Configuration);

// Configure Real-Time Streaming (SignalR Hubs & Tick Broadcaster)
builder.Services.AddSignalR();
builder.Services.AddHostedService<MarketTickBroadcaster>();

// Configure OpenAPI
builder.Services.AddOpenApi();

// Configure CORS for local development with Vite dev server
const string DevCorsPolicy = "DevCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:4173", "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Initialize and seed database
await app.Services.InitializeDatabaseAsync();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("TradeMASter API Reference")
               .WithTheme(ScalarTheme.Moon);
    });
    app.UseCors(DevCorsPolicy);
}
else
{
    app.UseHttpsRedirection();
    
    // Support serving SvelteKit static export when placed in wwwroot (optional production mode)
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Map Endpoint Groups
app.MapHealthEndpoints();
app.MapWeatherEndpoints();
app.MapTodoEndpoints();
app.MapMarketEndpoints();
app.MapMarketIntelligenceEndpoints();
app.MapPortfolioEndpoints();
app.MapOrderEndpoints();
app.MapLivePortfolioPolicyEndpoints();
app.MapTradePlanEndpoints();
app.MapAgentEndpoints();
app.MapBacktestEndpoints();
app.MapRobinhoodEndpoints();
app.MapOptimizationEndpoints();

// Map SignalR Real-Time Hubs
app.MapHub<AgentDebateHub>("/hubs/debate");
app.MapHub<MarketDataHub>("/hubs/market");

// API metadata endpoint; leave the production root available to the static Svelte application.
app.MapGet("/api", () => Results.Json(new
{
    name = "TradeMASter API",
    status = "Online",
    docs = "/scalar/v1",
    endpoints = new[]
    {
        "/api/health",
        "/api/robinhood/status",
        "/api/robinhood/holdings",
        "/api/optimizer/schedule",
        "/api/trade-plans/latest",
        "/api/portfolio",
        "/api/agents/history",
        "/api/backtest/strategies"
    },
    hubs = new[]
    {
        "/hubs/debate",
        "/hubs/market"
    }
}));

// Fallback to index.html for SPA routing in production if wwwroot exists
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();
