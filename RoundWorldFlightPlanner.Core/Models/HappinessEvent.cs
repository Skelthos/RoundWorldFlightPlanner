namespace RoundWorldFlightPlanner.Core.Models;

/// <summary>Audit-log entry for a change to the wife's happiness score.</summary>
public class HappinessEvent
{
    public int Id { get; set; }
    public int ItineraryId { get; set; }
    public Itinerary? Itinerary { get; set; }

    public int? FlightLegId { get; set; }
    public FlightLeg? FlightLeg { get; set; }

    public int Delta { get; set; }
    public required string Reason { get; set; }
    public DateTime OccurredUtc { get; set; }
}
