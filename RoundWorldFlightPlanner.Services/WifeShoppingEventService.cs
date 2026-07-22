using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// On arrival at a big-enough destination, the wife goes shopping: 3-10 new outfits at 3-8 kg each,
/// which pile onto the persistent cargo manifest and make weight &amp; balance progressively harder.
/// </summary>
public class WifeShoppingEventService(IPopulationLookupService populationLookupService, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    public IReadOnlyList<CargoItem> MaybeGenerateShoppingHaul(FlightLeg completedLeg, Airport destination)
    {
        if (!populationLookupService.IsAboveShoppingThreshold(destination))
        {
            return [];
        }

        var outfitCount = _random.Next(3, 11); // 3-10 inclusive
        var haul = new List<CargoItem>(outfitCount);

        for (var i = 0; i < outfitCount; i++)
        {
            var weightKg = Math.Round(3 + _random.NextDouble() * 5, 1); // 3.0-8.0 kg
            haul.Add(new CargoItem
            {
                Description = $"Outfit from {destination.City}",
                WeightKg = weightKg,
                IsWifeOutfit = true,
                AcquiredOnLegId = completedLeg.Id,
            });
        }

        return haul;
    }
}
