namespace RoundWorldFlightPlanner.Core.Models;

public class WeightBalanceResult
{
    public double GrossWeightKg { get; set; }
    public double MaxGrossWeightKg { get; set; }
    public bool IsOverweight => GrossWeightKg > MaxGrossWeightKg;

    public double CargoWeightKg { get; set; }
    public double WifeOutfitWeightKg { get; set; }

    public double CgPositionM { get; set; }
    public double CgForwardLimitM { get; set; }
    public double CgAftLimitM { get; set; }
    public bool IsCgInEnvelope => CgPositionM >= CgForwardLimitM && CgPositionM <= CgAftLimitM;

    public bool IsWithinLimits => !IsOverweight && IsCgInEnvelope;
}
