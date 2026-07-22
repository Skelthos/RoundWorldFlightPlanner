namespace RoundWorldFlightPlanner.Core.Models;

/// <summary>
/// One recorded position sample from a live SimConnect session while a leg is airborne - the actual
/// flown path, as opposed to the single straight planned line between a leg's two airports.
/// </summary>
public class FlightTrackPoint
{
    public int Id { get; set; }
    public int FlightLegId { get; set; }
    public FlightLeg? FlightLeg { get; set; }

    public DateTime TimestampUtc { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeFt { get; set; }
    public double GroundSpeedKts { get; set; }
    public double VerticalSpeedFpm { get; set; }
    public double HeadingDeg { get; set; }
}
