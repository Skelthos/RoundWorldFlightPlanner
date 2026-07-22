using RoundWorldFlightPlanner.Core.Enums;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Stub population check: large/medium airports are assumed to serve a city over 10,000 people.
/// TODO: replace with a real city-population dataset lookup.
/// </summary>
public class PopulationLookupService : IPopulationLookupService
{
    public bool IsAboveShoppingThreshold(Airport airport) =>
        airport.SizeClass is AirportSizeClass.LargeAirport or AirportSizeClass.MediumAirport;
}
