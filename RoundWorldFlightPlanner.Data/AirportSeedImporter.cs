using System.Globalization;
using System.Reflection;
using RoundWorldFlightPlanner.Core.Enums;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Data;

/// <summary>
/// Loads the bundled OurAirports (CC0) airport subset into the database, and tags the round-the-world
/// route's theme waypoints with their reserve name. Syncs on every launch, not just an empty database -
/// the bundled dataset has grown since earlier sessions (small-airport inclusion, new named waypoints
/// like the Antarctica gateways), and a database seeded before one of those additions would otherwise
/// be permanently missing airports that later code references, causing a lookup crash.
/// </summary>
public static class AirportSeedImporter
{
    /// <summary>ICAO -&gt; theHunter: Call of the Wild reserve this airport stands in for.</summary>
    public const string HomeIcao = "KVGT";

    public static readonly IReadOnlyDictionary<string, string> ReserveWaypoints = new Dictionary<string, string>
    {
        ["KOLM"] = "Layton Lake District",
        ["PAFA"] = "Yukon Valley",
        ["KDEN"] = "Silver Ridge Peaks",
        ["KLEB"] = "New England Mountains",
        ["KJAN"] = "Mississippi Acres Preserve",
        ["MMCU"] = "Rancho del Arroyo",
        ["SPZO"] = "Parque Fernando / Peru Reserve",
        ["FBSK"] = "Vurhonga Savanna",
        ["LEMD"] = "Cuatro Colinas",
        ["EDDH"] = "Salzwiesen Park",
        ["EDDM"] = "Hirschfelden",
        ["EFRO"] = "Revontuli Coast",
        ["UHMM"] = "Medved-Taiga National Park",
        ["VNKT"] = "Sundarpatan",
        ["YBCS"] = "Emerald Coast",
        ["NZRO"] = "Te Awaroa National Park",
    };

    public static void EnsureSeeded(FlightPlannerDbContext context)
    {
        var existingByIcao = context.Airports.ToDictionary(a => a.Icao);
        var changed = false;

        foreach (var airport in ReadEmbeddedAirports())
        {
            if (!existingByIcao.TryGetValue(airport.Icao, out var existing))
            {
                context.Airports.Add(airport);
                existingByIcao[airport.Icao] = airport; // guard against any duplicate ICAO rows within the CSV itself
                changed = true;
                continue;
            }

            // Corrects data-quality fixes to the bundled CSV (e.g. a misclassified continent code) on
            // an already-seeded database, not just brand-new rows - otherwise a fix here would silently
            // never reach anyone who already has a database on disk.
            if (existing.ContinentCode != airport.ContinentCode)
            {
                existing.ContinentCode = airport.ContinentCode;
                changed = true;
            }
        }

        if (changed)
        {
            context.SaveChanges();
        }
    }

    private static IEnumerable<Airport> ReadEmbeddedAirports()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("airports.csv", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);

        reader.ReadLine(); // header

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Length < 11)
            {
                continue;
            }

            var icao = fields[0];
            yield return new Airport
            {
                Icao = icao,
                Name = fields[1],
                City = fields[2],
                Country = fields[3],
                Latitude = double.Parse(fields[4], CultureInfo.InvariantCulture),
                Longitude = double.Parse(fields[5], CultureInfo.InvariantCulture),
                ElevationFt = int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var elev) ? elev : 0,
                SizeClass = fields[7] switch
                {
                    "large_airport" => AirportSizeClass.LargeAirport,
                    "medium_airport" => AirportSizeClass.MediumAirport,
                    _ => AirportSizeClass.SmallAirport,
                },
                ContinentCode = fields[8],
                LongestRunwayFt = int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var runwayFt) ? runwayFt : 0,
                IsPavedRunway = fields[10] == "1",
                ReserveName = ReserveWaypoints.GetValueOrDefault(icao),
            };
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
