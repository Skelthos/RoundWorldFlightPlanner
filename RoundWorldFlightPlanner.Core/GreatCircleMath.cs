using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Core;

/// <summary>Pure great-circle math shared by the Data seeder and the Services-layer distance service.</summary>
public static class GreatCircleMath
{
    public const double EarthRadiusNm = 3440.065;

    /// <summary>Long hops that cross a continent boundary or exceed this distance are suggested as ocean/desert crossings by default - editable by the pilot.</summary>
    private const double LikelyCrossingDistanceNm = 1500;

    public static double DistanceNm(Airport from, Airport to)
    {
        var (lat1, lon1) = (ToRadians(from.Latitude), ToRadians(from.Longitude));
        var (lat2, lon2) = (ToRadians(to.Latitude), ToRadians(to.Longitude));

        var dLat = lat2 - lat1;
        var dLon = lon2 - lon1;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusNm * c;
    }

    public static double InitialBearingDeg(Airport from, Airport to)
    {
        var (lat1, lon1) = (ToRadians(from.Latitude), ToRadians(from.Longitude));
        var (lat2, lon2) = (ToRadians(to.Latitude), ToRadians(to.Longitude));

        var dLon = lon2 - lon1;

        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var bearing = Math.Atan2(y, x) * 180.0 / Math.PI;

        return (bearing + 360) % 360;
    }

    public static TimeSpan EstimateTimeEnroute(double distanceNm, double cruiseSpeedKts)
    {
        if (cruiseSpeedKts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cruiseSpeedKts), "Cruise speed must be positive.");
        }

        return TimeSpan.FromHours(distanceNm / cruiseSpeedKts);
    }

    /// <summary>Best-effort default: a genuine continent-boundary crossing or a very long hop is probably over open ocean/desert.</summary>
    public static bool SuggestIsOceanCrossing(Airport from, Airport to, double distanceNm) =>
        from.ContinentCode != to.ContinentCode || distanceNm > LikelyCrossingDistanceNm;

    /// <summary>
    /// Perpendicular distance (nm) of <paramref name="point"/> off the great-circle path from
    /// <paramref name="start"/> to <paramref name="end"/>. Used to prefer stepping-stone candidates
    /// that stay near the direct line rather than detouring toward whatever's simply the farthest
    /// reachable point.
    /// </summary>
    public static double CrossTrackDistanceNm(Airport start, Airport end, Airport point)
    {
        var distanceStartToPoint = DistanceNm(start, point) / EarthRadiusNm;
        var bearingStartToPoint = ToRadians(InitialBearingDeg(start, point));
        var bearingStartToEnd = ToRadians(InitialBearingDeg(start, end));

        var crossTrack = Math.Asin(Math.Sin(distanceStartToPoint) * Math.Sin(bearingStartToPoint - bearingStartToEnd));
        return Math.Abs(crossTrack) * EarthRadiusNm;
    }

    /// <summary>Normalizes a longitude delta to (-180, 180] - negative means westward, positive means eastward.</summary>
    public static double NormalizeLongitudeDelta(double deltaDeg)
    {
        var normalized = deltaDeg % 360;
        if (normalized > 180)
        {
            normalized -= 360;
        }
        else if (normalized < -180)
        {
            normalized += 360;
        }

        return normalized;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
