using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Estimates takeoff/landing distance at an actual (below max gross) weight from a single POH
/// reference point, using the standard square-law approximation (required distance scales roughly
/// with the square of weight for a fixed aircraft/wing/density altitude): distance(W) = distance_ref
/// * (W / MaxGrossWeight)^2. This is a simplified estimate for trip-planning purposes, not a
/// substitute for the real AFM/POH performance charts (which also account for density altitude,
/// wind, slope, and surface condition).
/// </summary>
public class RunwayPerformanceService
{
    public RunwayPerformanceResult EvaluateTakeoff(Aircraft aircraft, Airport airport, double actualWeightKg) =>
        Evaluate(aircraft, airport, actualWeightKg, aircraft.TakeoffDistanceFtAtMaxGrossWeight);

    public RunwayPerformanceResult EvaluateLanding(Aircraft aircraft, Airport airport, double actualWeightKg) =>
        Evaluate(aircraft, airport, actualWeightKg, aircraft.LandingDistanceFtAtMaxGrossWeight);

    private static RunwayPerformanceResult Evaluate(Aircraft aircraft, Airport airport, double actualWeightKg, double referenceDistanceFt)
    {
        var weightRatio = aircraft.MaxGrossWeightKg > 0 ? actualWeightKg / aircraft.MaxGrossWeightKg : 1.0;
        var requiredDistanceFt = referenceDistanceFt * weightRatio * weightRatio;

        var maxWeightForRunwayKg = referenceDistanceFt > 0
            ? aircraft.MaxGrossWeightKg * Math.Sqrt(airport.LongestRunwayFt / referenceDistanceFt)
            : aircraft.MaxGrossWeightKg;
        maxWeightForRunwayKg = Math.Min(maxWeightForRunwayKg, aircraft.MaxGrossWeightKg);

        return new RunwayPerformanceResult
        {
            RunwayLengthFt = airport.LongestRunwayFt,
            IsPaved = airport.IsPavedRunway,
            IsRunwayDataKnown = airport.LongestRunwayFt > 0,
            ActualWeightKg = actualWeightKg,
            RequiredDistanceFt = requiredDistanceFt,
            MaxWeightForRunwayKg = maxWeightForRunwayKg,
        };
    }
}
