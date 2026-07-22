using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Finds which known airport a raw lat/lon position actually corresponds to - the missing piece for
/// turning a live SimConnect touchdown position into "the pilot actually landed at KXXX," including
/// when that differs from whatever airport the itinerary had planned (a diversion).
/// </summary>
public class AirportLookupService(IRouteDistanceService distanceService)
{
    /// <summary>
    /// Nearest known airport to <paramref name="latitude"/>/<paramref name="longitude"/>, or null if
    /// nothing is within <paramref name="maxDistanceNm"/> - a genuine off-airport or unlisted-strip
    /// landing, which should be surfaced for the pilot to confirm manually rather than guessed at.
    /// </summary>
    public Airport? FindNearestAirport(double latitude, double longitude, IReadOnlyList<Airport> allAirports, double maxDistanceNm)
    {
        var position = new Airport
        {
            Icao = string.Empty,
            Name = string.Empty,
            City = string.Empty,
            Country = string.Empty,
            ContinentCode = string.Empty,
            Latitude = latitude,
            Longitude = longitude,
        };

        Airport? nearest = null;
        var nearestDistanceNm = double.MaxValue;

        foreach (var airport in allAirports)
        {
            var distanceNm = distanceService.GreatCircleDistanceNm(position, airport);
            if (distanceNm < nearestDistanceNm)
            {
                nearestDistanceNm = distanceNm;
                nearest = airport;
            }
        }

        return nearestDistanceNm <= maxDistanceNm ? nearest : null;
    }
}
