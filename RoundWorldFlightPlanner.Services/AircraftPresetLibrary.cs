using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Starting aircraft profiles. Figures are approximate public specs, not a real POH - all editable
/// in Aircraft Setup once applied.
/// </summary>
public static class AircraftPresetLibrary
{
    public static Aircraft CreateKingAir350I() => new()
    {
        Name = "Beechcraft King Air 350i",
        EmptyWeightKg = 4400,
        EmptyWeightArmM = 4.50,
        MaxGrossWeightKg = 6800,
        FuelCapacityKg = 1638,
        FuelBurnKgPerHour = 320,
        FuelArmM = 4.60,
        PilotAndPaxWeightKg = 320,
        PilotAndPaxArmM = 4.20,
        CargoArmM = 5.50,
        CruiseSpeedKts = 310,
        CgForwardLimitM = 4.30,
        CgAftLimitM = 4.90,
        // POH balanced-field figures at max gross weight (per user-supplied POH reference).
        TakeoffDistanceFtAtMaxGrossWeight = 3300,
        LandingDistanceFtAtMaxGrossWeight = 2692,
    };

    public static Aircraft CreateCessna206Stationair() => new()
    {
        Name = "Cessna 206 Stationair",
        EmptyWeightKg = 940,
        EmptyWeightArmM = 0.90,
        MaxGrossWeightKg = 1633,
        FuelCapacityKg = 250,
        FuelBurnKgPerHour = 30,
        FuelArmM = 1.00,
        PilotAndPaxWeightKg = 170,
        PilotAndPaxArmM = 0.85,
        CargoArmM = 1.30,
        CruiseSpeedKts = 140,
        CgForwardLimitM = 0.95,
        CgAftLimitM = 1.15,
        // POH total distance over a 50ft obstacle at max gross weight (public Cessna 206H figures).
        TakeoffDistanceFtAtMaxGrossWeight = 1835,
        LandingDistanceFtAtMaxGrossWeight = 1350,
    };

    public static IReadOnlyList<(string Name, Func<Aircraft> Create)> Presets { get; } =
    [
        ("Beechcraft King Air 350i (default)", CreateKingAir350I),
        ("Cessna 206 Stationair", CreateCessna206Stationair),
    ];
}
