using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>Turns a parsed <see cref="WeatherSnapshot"/> into a plain-English summary, for pilots who don't read raw METAR.</summary>
public static class MetarTranslator
{
    public static string ToPlainEnglish(WeatherSnapshot weather)
    {
        var parts = new List<string>
        {
            weather.WindSpeedKts == 0
                ? "Wind calm"
                : weather.WindGustKts > weather.WindSpeedKts
                    ? $"Wind {weather.WindDirectionDeg:000}° at {weather.WindSpeedKts}kt, gusting {weather.WindGustKts}kt"
                    : $"Wind {weather.WindDirectionDeg:000}° at {weather.WindSpeedKts}kt",
            weather.VisibilitySm >= 10 ? "visibility 10sm+" : $"visibility {weather.VisibilitySm:0.#}sm",
            $"sky {DescribeSky(weather.CloudLayers)}",
            $"temp {weather.TemperatureC:0}°C" + (weather.DewpointC is { } dewpointC ? $" / dew {dewpointC:0}°C" : string.Empty),
        };

        if (weather.AltimeterHpa is { } altimeterHpa)
        {
            parts.Add($"altimeter {altimeterHpa:0}hPa");
        }

        return string.Join(", ", parts);
    }

    private static string DescribeSky(IReadOnlyList<CloudLayer> layers)
    {
        if (layers.Count == 0)
        {
            return "clear";
        }

        return string.Join(", ", layers.Select(layer => layer.BaseFt is { } baseFt
            ? $"{DescribeCover(layer.Cover)} {baseFt:N0}ft"
            : DescribeCover(layer.Cover)));
    }

    private static string DescribeCover(string cover) => cover switch
    {
        "SKC" or "CLR" or "NSC" or "NCD" => "clear",
        "FEW" => "few clouds",
        "SCT" => "scattered clouds",
        "BKN" => "broken clouds",
        "OVC" => "overcast",
        "VV" => "vertical visibility (obscured)",
        _ => cover,
    };
}
