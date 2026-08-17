namespace TradeMASter.Api.Models;

public record ComponentStatus(string Status, string Details);

public record HealthInfo(
    string Status,
    string FrameworkVersion,
    DateTime ServerTimeUtc,
    TimeSpan Uptime,
    string Environment,
    Dictionary<string, ComponentStatus>? Components = null
);
