using RoundWorldFlightPlanner.Core.Enums;

namespace RoundWorldFlightPlanner.Core.Models;

public class Airport
{
    public int Id { get; set; }
    public required string Icao { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int ElevationFt { get; set; }
    public AirportSizeClass SizeClass { get; set; }

    /// <summary>OurAirports 2-letter continent code: AF, AN, AS, EU, NA, OC, SA.</summary>
    public required string ContinentCode { get; set; }

    /// <summary>Length of the longest non-closed runway, in feet. 0 if unknown.</summary>
    public int LongestRunwayFt { get; set; }

    /// <summary>Whether the longest runway is a paved surface (asphalt/concrete) vs. grass/gravel/dirt/snow/etc.</summary>
    public bool IsPavedRunway { get; set; }

    /// <summary>Name of the theHunter: Call of the Wild reserve this airport stands in for, if it's one of the themed waypoints.</summary>
    public string? ReserveName { get; set; }
}
