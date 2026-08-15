using TradeMASter.Api.Endpoints;
using TradeMASter.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Dependency Injection
builder.Services.AddSingleton<ITodoService, InMemoryTodoService>();

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

// Root landing endpoint if accessed directly via browser
app.MapGet("/", () => Results.Json(new
{
    name = "TradeMASter API",
    status = "Online",
    docs = "/scalar/v1",
    endpoints = new[] { "/api/health", "/api/weather/forecast", "/api/todos" }
}));

// Fallback to index.html for SPA routing in production if wwwroot exists
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();
