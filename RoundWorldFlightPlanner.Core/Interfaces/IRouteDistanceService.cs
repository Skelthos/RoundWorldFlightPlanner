using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Core.Interfaces;

public interface IRouteDistanceService
{
    double GreatCircleDistanceNm(Airport from, Airport to);

    /// <summary>Initial great-circle bearing in degrees true, from -&gt; to.</summary>
    double InitialBearingDeg(Airport from, Airport to);

    TimeSpan EstimateTimeEnroute(double distanceNm, double cruiseSpeedKts);
}
