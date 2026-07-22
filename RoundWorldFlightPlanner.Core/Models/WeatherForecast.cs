namespace RoundWorldFlightPlanner.Core.Models;

/// <summary>A TAF forecast for an airport.</summary>
public class WeatherForecast
{
    public required string Icao { get; set; }
    public DateTime IssueTimeUtc { get; set; }
    public required string RawTaf { get; set; }
}
