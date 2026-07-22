using System.Reflection;

namespace RoundWorldFlightPlanner.Data;

/// <summary>
/// The list of (continent, country) pairs from OurAirports, filtered down to sovereign nations only -
/// used as the denominator for "% of countries visited per continent." OurAirports' country codes are
/// plain ISO 3166-1 codes, which includes dependent territories with their own code (Bermuda, Puerto
/// Rico, Hong Kong, Greenland, French Polynesia, etc.) alongside actual UN member states. Counting those
/// as separate "countries" toward the goal badly inflates the denominator relative to what a pilot
/// actually means by "country" - North America has only 23 sovereign nations, but 43 raw ISO entries
/// once every Caribbean/Atlantic/Pacific territory is counted too, making "hit 50%" a much harder and
/// less meaningful bar than intended. <see cref="NonSovereignCodes"/> excludes those, plus a couple of
/// invalid/placeholder codes that show up in the raw OurAirports data (e.g. "ZZ" for unset country).
/// </summary>
public static class ContinentCountryReference
{
    /// <summary>
    /// ISO country codes present in <c>continent_countries.csv</c> that are dependent territories, not
    /// UN member states - excluded from the country-goal denominator and from country-visited counting.
    /// A few borderline cases (Taiwan, Kosovo, Palestine, Cook Islands, Niue) are deliberately kept IN as
    /// countable: each has substantial real-world self-governance and its own distinct ICAO/airspace
    /// identity, which matters more for this app's purposes than formal UN membership.
    /// </summary>
    private static readonly HashSet<string> NonSovereignCodes =
    [
        // Africa
        "EH", // Western Sahara (disputed, no seated UN member government)
        "RE", // Réunion (France)
        "SH", // Saint Helena, Ascension and Tristan da Cunha (UK)
        "TF", // French Southern and Antarctic Lands (France)
        "YT", // Mayotte (France)
        "ZZ", // invalid/unset placeholder code, not a real country
        // Antarctica
        "AQ", // Antarctica itself - no recognized sovereign government
        "GS", // South Georgia and the South Sandwich Islands (UK)
        // Asia
        "CC", // Cocos (Keeling) Islands (Australia)
        "CX", // Christmas Island (Australia)
        "HK", // Hong Kong (China SAR)
        "IO", // British Indian Ocean Territory (UK)
        "MO", // Macau (China SAR)
        "XP", // placeholder/unclassified code
        // Europe
        "FO", // Faroe Islands (Denmark)
        "GG", // Guernsey (UK Crown Dependency)
        "GI", // Gibraltar (UK)
        "IM", // Isle of Man (UK Crown Dependency)
        "JE", // Jersey (UK Crown Dependency)
        // North America
        "AI", // Anguilla (UK)
        "AW", // Aruba (Netherlands)
        "BL", // Saint Barthélemy (France)
        "BM", // Bermuda (UK)
        "BQ", // Bonaire, Sint Eustatius and Saba (Netherlands)
        "CW", // Curaçao (Netherlands)
        "GL", // Greenland (Denmark)
        "GP", // Guadeloupe (France)
        "KY", // Cayman Islands (UK)
        "MF", // Saint Martin (France)
        "MQ", // Martinique (France)
        "MS", // Montserrat (UK)
        "PM", // Saint Pierre and Miquelon (France)
        "PR", // Puerto Rico (US)
        "SX", // Sint Maarten (Netherlands)
        "TC", // Turks and Caicos Islands (UK)
        "VG", // British Virgin Islands (UK)
        "VI", // US Virgin Islands (US)
        // Oceania
        "AS", // American Samoa (US) - not to be confused with the "AS" continent code (Asia)
        "GU", // Guam (US)
        "HM", // Heard Island and McDonald Islands (Australia)
        "MP", // Northern Mariana Islands (US)
        "NC", // New Caledonia (France)
        "NF", // Norfolk Island (Australia)
        "PF", // French Polynesia (France)
        "UM", // United States Minor Outlying Islands (US)
        "WF", // Wallis and Futuna (France)
        // South America
        "FK", // Falkland Islands (UK)
        "GF", // French Guiana (France)
    ];

    /// <summary>
    /// (continent, country) pairs to drop even though the country is sovereign - for a handful of
    /// mainland South American countries with one outlying Caribbean island (Colombia's San Andrés,
    /// Venezuela's Isla Margarita) tagged continent "NA" in the underlying airport data. Landing there
    /// only counts toward the country's actual home continent (South America), not North America too.
    /// </summary>
    private static readonly HashSet<(string Continent, string Country)> PrimaryContinentOnlyPairs =
    [
        ("NA", "CO"), // Colombia - counts toward South America only
        ("NA", "VE"), // Venezuela - counts toward South America only
    ];

    private static IReadOnlyDictionary<string, IReadOnlySet<string>>? _cache;

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> CountriesByContinent =>
        _cache ??= Load();

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("continent_countries.csv", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);

        var result = new Dictionary<string, HashSet<string>>();
        reader.ReadLine(); // header

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split(',');
            if (fields.Length < 2)
            {
                continue;
            }

            var continent = fields[0].Trim('"');
            var country = fields[1].Trim('"');

            // Ensure the continent has an entry even if every one of its rows gets filtered out below
            // (Antarctica's whole raw list - AQ/GS/TF - is non-sovereign territory, so without this it
            // would vanish from the dictionary entirely instead of correctly showing 0/0 special-cased).
            if (!result.TryGetValue(continent, out var countries))
            {
                countries = [];
                result[continent] = countries;
            }

            if (NonSovereignCodes.Contains(country) || PrimaryContinentOnlyPairs.Contains((continent, country)))
            {
                continue;
            }

            countries.Add(country);
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
    }
}
