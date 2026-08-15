using TradeMASter.Api.Models;

namespace TradeMASter.Api.Endpoints;

public static class WeatherEndpoints
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    public static RouteGroupBuilder MapWeatherEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/weather")
            .WithTags("Weather");

        group.MapGet("/forecast", (int? days) =>
        {
            var count = Math.Clamp(days ?? 5, 1, 14);
            var forecast = Enumerable.Range(1, count).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-10, 38),
                    Summaries[Random.Shared.Next(Summaries.Length)]
                ))
                .ToArray();

            return Results.Ok(forecast);
        })
        .WithName("GetWeatherForecast")
        .WithSummary("Generates random weather forecasts for sample communication testing")
        .Produces<WeatherForecast[]>(StatusCodes.Status200OK);

        return group;
    }
}
