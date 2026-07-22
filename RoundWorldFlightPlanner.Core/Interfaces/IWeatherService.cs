using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Core.Interfaces;

public interface IWeatherService
{
    Task<WeatherSnapshot?> GetCurrentWeatherAsync(string icao, CancellationToken cancellationToken = default);

    Task<WeatherForecast?> GetForecastAsync(string icao, CancellationToken cancellationToken = default);

    /// <summary>Raw AIRMET/SIGMET text currently in effect, US/NWS-scoped coverage only.</summary>
    Task<IReadOnlyList<string>> GetEnrouteHazardsAsync(CancellationToken cancellationToken = default);
}
