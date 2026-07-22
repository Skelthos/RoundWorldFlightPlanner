using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Core.Interfaces;

public interface IPopulationLookupService
{
    /// <summary>
    /// Whether the airport's city is "over 10,000 people" for wife-shopping-event purposes.
    /// TODO: currently a stub proxied off <see cref="Airport.SizeClass"/> - swap in a real
    /// city population dataset once one is sourced.
    /// </summary>
    bool IsAboveShoppingThreshold(Airport airport);
}
