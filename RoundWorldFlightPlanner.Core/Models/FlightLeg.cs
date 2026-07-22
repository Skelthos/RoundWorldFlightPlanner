using RoundWorldFlightPlanner.Core.Enums;

namespace RoundWorldFlightPlanner.Core.Models;

public class FlightLeg
{
    public int Id { get; set; }
    public int ItineraryId { get; set; }
    public Itinerary? Itinerary { get; set; }

    public int SequenceNumber { get; set; }

    public int DepartureAirportId { get; set; }
    public Airport? DepartureAirport { get; set; }

    public int ArrivalAirportId { get; set; }
    public Airport? ArrivalAirport { get; set; }

    public double PlannedDistanceNm { get; set; }
    public double PlannedCruiseSpeedKts { get; set; }

    /// <summary>Exempt from the itinerary's max-flight-time rule - a genuine ocean/desert crossing with nowhere to stop partway.</summary>
    public bool IsOceanCrossing { get; set; }

    /// <summary>
    /// True only for the original named-waypoint legs from <c>ItinerarySeeder</c>'s narrative spine.
    /// False for anything auto-generated later (country-fill stops, split-in fuel stops). Lets
    /// "Refactor Route" strip back to the pure spine and regenerate fresh, rather than only adding
    /// what's missing against an already-compliant-but-messy existing route.
    /// </summary>
    public bool IsSpineAnchor { get; set; }

    public FlightPhase Phase { get; set; } = FlightPhase.NotStarted;

    public DateTime? EngineStartUtc { get; set; }
    public DateTime? TakeoffUtc { get; set; }
    public DateTime? LandingUtc { get; set; }
    public DateTime? ShutDownUtc { get; set; }

    /// <summary>Recorded touchdown vertical speed in feet per minute (negative = descending), entered by the pilot on arrival.</summary>
    public double? TouchdownVerticalSpeedFpm { get; set; }

    /// <summary>Days spent on the ground here beyond a normal turnaround - e.g. a forced wife-happiness shopping layover.</summary>
    public int LayoverDays { get; set; }

    /// <summary>Which aircraft actually flew this leg. Null means "whatever the itinerary's current aircraft is" - set explicitly once flown, so switching aircraft later doesn't rewrite history.</summary>
    public int? AircraftId { get; set; }
    public Aircraft? Aircraft { get; set; }

    public double? SimBriefFuelPlanKg { get; set; }
    public string? SimBriefAlternateIcao { get; set; }
    public string? SimBriefRouteText { get; set; }
    public DateTime? SimBriefGeneratedUtc { get; set; }

    /// <summary>Recorded SimConnect position samples while this leg was airborne - the actual flown path.</summary>
    public ICollection<FlightTrackPoint> TrackPoints { get; set; } = [];

    /// <summary>Full engine-start-to-shutdown block time. Null until the leg is complete.</summary>
    public TimeSpan? BlockTime => EngineStartUtc.HasValue && ShutDownUtc.HasValue
        ? ShutDownUtc.Value - EngineStartUtc.Value
        : null;

    public bool IsComplete => Phase == FlightPhase.ShutDown;
}
