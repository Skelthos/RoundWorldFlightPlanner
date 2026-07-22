using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

public class GreatCircleService : IRouteDistanceService
{
    public double GreatCircleDistanceNm(Airport from, Airport to) => GreatCircleMath.DistanceNm(from, to);

    public double InitialBearingDeg(Airport from, Airport to) => GreatCircleMath.InitialBearingDeg(from, to);

    public TimeSpan EstimateTimeEnroute(double distanceNm, double cruiseSpeedKts) =>
        GreatCircleMath.EstimateTimeEnroute(distanceNm, cruiseSpeedKts);
}
