namespace RoundWorldFlightPlanner.Core.Models;

public class RunwayPerformanceResult
{
    public int RunwayLengthFt { get; set; }
    public bool IsPaved { get; set; }
    public bool IsRunwayDataKnown { get; set; }

    public double ActualWeightKg { get; set; }
    public double RequiredDistanceFt { get; set; }
    public double MaxWeightForRunwayKg { get; set; }

    /// <summary>True if the runway is long enough for the actual weight. Meaningless when IsRunwayDataKnown is false.</summary>
    public bool IsSufficient => IsRunwayDataKnown && RequiredDistanceFt <= RunwayLengthFt;
}
