namespace TradeMASter.Api.Models;

public record HealthInfo(
    string Status,
    string FrameworkVersion,
    DateTime ServerTimeUtc,
    TimeSpan Uptime,
    string Environment
);
