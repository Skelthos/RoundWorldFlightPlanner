using System.Reflection;
using System.Text.Json;

namespace RoundWorldFlightPlanner.UI.Controls;

/// <summary>
/// Coastline rings for the map, from the embedded Natural Earth 110m land dataset (public domain).
/// Each ring is a closed loop of (longitude, latitude) points; Natural Earth already splits polygons at
/// the antimeridian, so consecutive points never jump across the map.
/// </summary>
public static class LandOutlines
{
    private static readonly Lazy<IReadOnlyList<(double Lon, double Lat)[]>> Rings = new(Load);

    public static IReadOnlyList<(double Lon, double Lat)[]> All => Rings.Value;

    private static IReadOnlyList<(double Lon, double Lat)[]> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("land.geojson", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return [];
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);

        var rings = new List<(double, double)[]>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var geometry = feature.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            switch (geometry.GetProperty("type").GetString())
            {
                case "Polygon":
                    AddPolygon(coordinates, rings);
                    break;
                case "MultiPolygon":
                    foreach (var polygon in coordinates.EnumerateArray())
                    {
                        AddPolygon(polygon, rings);
                    }

                    break;
            }
        }

        return rings;
    }

    /// <summary>Only the outer ring of each polygon - holes (lakes) would just be clutter at this scale.</summary>
    private static void AddPolygon(JsonElement polygon, List<(double, double)[]> rings)
    {
        var outer = polygon.EnumerateArray().First();
        rings.Add(outer.EnumerateArray()
            .Select(p => (p[0].GetDouble(), p[1].GetDouble()))
            .ToArray());
    }
}
