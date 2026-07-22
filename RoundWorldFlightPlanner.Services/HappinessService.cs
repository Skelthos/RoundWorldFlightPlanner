using System.Text.RegularExpressions;
using RoundWorldFlightPlanner.Core.Enums;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Tracks the wife's happiness (0-100). Landings, hunting-reserve stops, weather, shopping hauls,
/// and baggage offloads all move it; below 25 forces a shopping layover to bring it back up.
/// </summary>
public partial class HappinessService
{
    public const int ForcedLayoverThreshold = 25;

    // Volanta-style vertical-speed bands, in fpm (negative = descending). Tunable.
    private const double GreaserThresholdFpm = -120;
    private const double SmoothThresholdFpm = -240;
    private const double FirmThresholdFpm = -600;
    private const double HardThresholdFpm = -900;

    public LandingQuality ClassifyLanding(double verticalSpeedFpm) => verticalSpeedFpm switch
    {
        >= GreaserThresholdFpm => LandingQuality.Greaser,
        >= SmoothThresholdFpm => LandingQuality.Smooth,
        >= FirmThresholdFpm => LandingQuality.Firm,
        >= HardThresholdFpm => LandingQuality.Hard,
        _ => LandingQuality.Severe,
    };

    public HappinessEvent RecordLanding(Itinerary itinerary, FlightLeg leg, double verticalSpeedFpm)
    {
        var quality = ClassifyLanding(verticalSpeedFpm);
        var delta = quality switch
        {
            LandingQuality.Greaser => 10,
            LandingQuality.Smooth => 5,
            LandingQuality.Firm => 0,
            LandingQuality.Hard => -10,
            LandingQuality.Severe => -20,
            _ => 0,
        };

        return Apply(itinerary, leg, delta, $"{quality} landing ({verticalSpeedFpm:F0} fpm)");
    }

    public HappinessEvent? RecordHuntingTrip(Itinerary itinerary, FlightLeg leg)
    {
        if (leg.ArrivalAirport?.ReserveName is not { } reserveName)
        {
            return null;
        }

        return Apply(itinerary, leg, -5, $"Another hunting stop: {reserveName}");
    }

    public HappinessEvent RecordShoppingHaul(Itinerary itinerary, FlightLeg leg, IReadOnlyCollection<CargoItem> haul)
    {
        var delta = haul.Count * 2;
        return Apply(itinerary, leg, delta, $"Shopping haul: {haul.Count} outfits");
    }

    public HappinessEvent RecordBaggageOffload(Itinerary itinerary, FlightLeg leg, int itemCount)
    {
        var delta = -itemCount * 3;
        return Apply(itinerary, leg, delta, $"Had to ship {itemCount} item(s) home for weight & balance");
    }

    public HappinessEvent? RecordWeatherPenalty(Itinerary itinerary, FlightLeg leg, WeatherSnapshot weather)
    {
        var isAdverse = AdverseWeatherRegex().IsMatch(weather.RawMetar)
            || weather.VisibilitySm is > 0 and < 3
            || GustRegex().Match(weather.RawMetar) is { Success: true } gustMatch && int.Parse(gustMatch.Groups[1].Value) >= 25;

        if (!isAdverse)
        {
            return null;
        }

        return Apply(itinerary, leg, -8, $"Rough weather on arrival: {weather.RawMetar}");
    }

    public bool RequiresForcedShoppingLayover(Itinerary itinerary) => itinerary.HappinessScore < ForcedLayoverThreshold;

    public HappinessEvent ApplyForcedLayover(Itinerary itinerary, FlightLeg leg, Airport majorMetroAirport)
    {
        leg.LayoverDays = 7;
        return Apply(itinerary, leg, 35, $"Forced week-long shopping layover in {majorMetroAirport.City}");
    }

    private static HappinessEvent Apply(Itinerary itinerary, FlightLeg leg, int delta, string reason)
    {
        itinerary.HappinessScore += delta;

        var happinessEvent = new HappinessEvent
        {
            ItineraryId = itinerary.Id,
            FlightLegId = leg.Id == 0 ? null : leg.Id,
            Delta = delta,
            Reason = reason,
            OccurredUtc = DateTime.UtcNow,
        };

        itinerary.HappinessEvents.Add(happinessEvent);
        return happinessEvent;
    }

    [GeneratedRegex(@"\bTS\b|FZRA|FZDZ")]
    private static partial Regex AdverseWeatherRegex();

    [GeneratedRegex(@"G(\d{2,3})KT")]
    private static partial Regex GustRegex();
}
