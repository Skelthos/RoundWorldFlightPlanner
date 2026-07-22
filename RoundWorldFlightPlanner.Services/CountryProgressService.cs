using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;

namespace RoundWorldFlightPlanner.Services;

public class CountryProgressService
{
    private const string AntarcticaCode = "AN";

    private static readonly IReadOnlyDictionary<string, string> ContinentNames = new Dictionary<string, string>
    {
        ["AF"] = "Africa",
        ["AN"] = "Antarctica",
        ["AS"] = "Asia",
        ["EU"] = "Europe",
        ["NA"] = "North America",
        ["OC"] = "Oceania",
        ["SA"] = "South America",
    };

    public IReadOnlyList<ContinentProgress> GetProgress(Itinerary itinerary)
    {
        var visitedByContinent = itinerary.Legs
            .Where(l => l.IsComplete && l.ArrivalAirport is not null)
            .Select(l => l.ArrivalAirport!)
            .GroupBy(a => a.ContinentCode)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Country).ToHashSet());

        var reference = ContinentCountryReference.CountriesByContinent;

        return reference
            .Select(kv =>
            {
                var continentCode = kv.Key;
                var totalCountries = kv.Value.Count;
                var isAntarctica = continentCode == AntarcticaCode;

                // Only landings in countries that are actually in the (sovereign-nations-only)
                // reference count toward progress - a stop at a territory like Bermuda or Greenland
                // still happened, it just isn't one of the countries the goal is tracking. Antarctica is
                // the exception: it has zero sovereign nations by definition, so its own reference set is
                // always empty - "landed once" has to be checked by continent alone, or intersecting
                // against an empty set would make it permanently look unvisited.
                var visitedCount = isAntarctica
                    ? visitedByContinent.GetValueOrDefault(continentCode, []).Count
                    : visitedByContinent.GetValueOrDefault(continentCode, []).Count(kv.Value.Contains);

                return new ContinentProgress
                {
                    ContinentCode = continentCode,
                    ContinentName = ContinentNames.GetValueOrDefault(continentCode, continentCode),
                    CountriesVisited = visitedCount,
                    CountriesTotal = totalCountries,
                    IsSpecialCase = isAntarctica,
                    GoalMet = isAntarctica
                        ? visitedCount > 0
                        : totalCountries > 0 && 100.0 * visitedCount / totalCountries >= itinerary.CountryGoalPercent,
                };
            })
            .OrderBy(p => p.ContinentName)
            .ToList();
    }
}
