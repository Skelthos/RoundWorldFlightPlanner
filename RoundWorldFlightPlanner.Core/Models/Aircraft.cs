namespace RoundWorldFlightPlanner.Core.Models;

/// <summary>
/// Simplified single-cargo-station weight &amp; balance profile. Arms are distances (meters) from
/// the aircraft's datum; moments are weight * arm. Good enough for a GA-style trip planner, not
/// a substitute for the real POH.
/// </summary>
public class Aircraft
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public double EmptyWeightKg { get; set; }
    public double EmptyWeightArmM { get; set; }

    public double MaxGrossWeightKg { get; set; }

    public double FuelCapacityKg { get; set; }
    public double FuelBurnKgPerHour { get; set; }
    public double FuelArmM { get; set; }

    public double PilotAndPaxWeightKg { get; set; }
    public double PilotAndPaxArmM { get; set; }

    public double CargoArmM { get; set; }

    public double CruiseSpeedKts { get; set; }

    public double CgForwardLimitM { get; set; }
    public double CgAftLimitM { get; set; }

    /// <summary>POH balanced-field takeoff distance at max gross weight, ft. Reference point for the runway-performance square-law estimate.</summary>
    public double TakeoffDistanceFtAtMaxGrossWeight { get; set; }

    /// <summary>POH landing distance at max gross weight, ft. Reference point for the runway-performance square-law estimate.</summary>
    public double LandingDistanceFtAtMaxGrossWeight { get; set; }
}
