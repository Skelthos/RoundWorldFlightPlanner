using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Enforces the round-the-world challenge rules: each new leg must depart from wherever the
/// previous leg landed, legs should stay under the itinerary's max flight time (ocean/desert
/// crossings exempt), and the route should keep making net westward progress (soft warning
/// only - a couple of local jogs are expected).
/// </summary>
public class ItineraryContinuityValidator(IRouteDistanceService distanceService)
{
    private const double EastwardToleranceDeg = 5.0;

    public static bool SuggestIsOceanCrossing(Airport departure, Airport arrival, double distanceNm) =>
        GreatCircleMath.SuggestIsOceanCrossing(departure, arrival, distanceNm);

    public LegValidationResult ValidateNewLeg(Itinerary itinerary, Airport departure, Airport arrival, bool isOceanCrossing = false)
    {
        var result = new LegValidationResult();

        var lastCompletedLeg = itinerary.Legs
            .Where(l => l.IsComplete)
            .OrderByDescending(l => l.SequenceNumber)
            .FirstOrDefault();

        var expectedDepartureIcao = lastCompletedLeg?.ArrivalAirport?.Icao ?? AirportSeedImporter.HomeIcao;

        if (!string.Equals(departure.Icao, expectedDepartureIcao, StringComparison.OrdinalIgnoreCase))
        {
            result.AddError(
                $"This leg must depart from {expectedDepartureIcao} (where the last flight landed), not {departure.Icao}.");
        }

        var distanceNm = distanceService.GreatCircleDistanceNm(departure, arrival);
        var estimatedCruiseTime = distanceService.EstimateTimeEnroute(distanceNm, itinerary.Aircraft?.CruiseSpeedKts ?? 0);
        var maxFlightTime = TimeSpan.FromHours(itinerary.MaxFlightTimeHours);
        if (!isOceanCrossing && estimatedCruiseTime > maxFlightTime)
        {
            result.AddWarning(
                $"Estimated cruise time is {estimatedCruiseTime:h\\:mm}, over the {itinerary.MaxFlightTimeHours:F1}-hour target. Consider an intermediate fuel stop, or flag this as an ocean/desert crossing if there's nowhere to stop.");
        }

        var longitudeDeltaDeg = GreatCircleMath.NormalizeLongitudeDelta(arrival.Longitude - departure.Longitude);
        if (longitudeDeltaDeg > EastwardToleranceDeg)
        {
            result.AddWarning(
                $"This leg moves {longitudeDeltaDeg:F0}° eastward, against the round-the-world westbound flow.");
        }

        return result;
    }
}
