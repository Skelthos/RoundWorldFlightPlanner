using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

public class WeightBalanceService
{
    public WeightBalanceResult Calculate(Aircraft aircraft, double fuelOnBoardKg, IReadOnlyCollection<CargoItem> cargo)
    {
        var cargoWeightKg = cargo.Sum(c => c.WeightKg);
        var wifeOutfitWeightKg = cargo.Where(c => c.IsWifeOutfit).Sum(c => c.WeightKg);

        var grossWeightKg = aircraft.EmptyWeightKg + fuelOnBoardKg + aircraft.PilotAndPaxWeightKg + cargoWeightKg;

        var totalMoment = aircraft.EmptyWeightKg * aircraft.EmptyWeightArmM
                         + fuelOnBoardKg * aircraft.FuelArmM
                         + aircraft.PilotAndPaxWeightKg * aircraft.PilotAndPaxArmM
                         + cargoWeightKg * aircraft.CargoArmM;

        var cgPositionM = grossWeightKg > 0 ? totalMoment / grossWeightKg : 0;

        return new WeightBalanceResult
        {
            GrossWeightKg = grossWeightKg,
            MaxGrossWeightKg = aircraft.MaxGrossWeightKg,
            CargoWeightKg = cargoWeightKg,
            WifeOutfitWeightKg = wifeOutfitWeightKg,
            CgPositionM = cgPositionM,
            CgForwardLimitM = aircraft.CgForwardLimitM,
            CgAftLimitM = aircraft.CgAftLimitM,
        };
    }
}
