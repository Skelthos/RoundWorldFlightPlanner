namespace RoundWorldFlightPlanner.Core.Models;

/// <summary>A pulled SimBrief operational flight plan (OFP) for a dispatched flight.</summary>
public class SimBriefOfp
{
    public required string OriginIcao { get; set; }
    public required string DestinationIcao { get; set; }
    public string? AlternateIcao { get; set; }
    public string? RouteText { get; set; }
    public int CruiseAltitudeFt { get; set; }
    public double PlanRampFuelKg { get; set; }
    public double PlanTripFuelKg { get; set; }
    public double PlanReserveFuelKg { get; set; }
    public DateTime GeneratedUtc { get; set; }
}
