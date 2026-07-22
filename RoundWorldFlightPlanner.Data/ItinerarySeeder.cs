using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Enums;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Data;

/// <summary>
/// Builds the default westbound KVGT -&gt; KVGT itinerary skeleton: reserve anchors plus a spine of
/// named waypoints matching the round-the-world narrative (Denver/California/Alaska, an Australia
/// circumnavigation, an Africa circumnavigation, Ireland, Greenland, the Caribbean, Antarctica,
/// Central America). This is a representative spine, not the full country list - use the
/// country-aware suggestions in AddLegForm during play to fill in enough additional stops to hit
/// your per-continent country goal.
/// </summary>
public static class ItinerarySeeder
{
    private static readonly string[] BaseIcaoOrder =
    [
        AirportSeedImporter.HomeIcao, // KVGT - depart
        "KDEN", // Silver Ridge Peaks (Denver)
        "KSFO", // California
        "KOLM", // Layton Lake District
        "PAFA", // Yukon Valley (Alaska)
        "RJTT", // Tokyo - down through eastern Asia
        "VHHH", // Hong Kong
        "WSSS", // Singapore - bridge into Oceania
        "YPDN", // Darwin - into Australia
        "YBCS", // Emerald Coast (Cairns)
        "YSSY", // Sydney - Australia circumnavigation
        "YMML", // Melbourne
        "YPAD", // Adelaide
        "YPPH", // Perth
        "NZRO", // Te Awaroa National Park (New Zealand)
        "NFFN", // Nadi, Fiji - back up through Oceania
        "VABB", // Mumbai - India
        "VNKT", // Sundarpatan (Nepal)
        "OMDB", // Dubai - Middle East
        "HECA", // Cairo - into Africa
        "HKJK", // Nairobi - Africa circumnavigation
        "FBSK", // Vurhonga Savanna (Gaborone)
        "FACT", // Cape Town
        "DNMM", // Lagos
        "GMMN", // Casablanca
        "LEMD", // Cuatro Colinas (Spain) - into Europe
        "EDDH", // Salzwiesen Park (Hamburg)
        "EDDM", // Hirschfelden (Munich)
        "EFRO", // Revontuli Coast (Rovaniemi)
        "EINN", // Ireland (Shannon)
        "BGSF", // Greenland (Kangerlussuaq)
        "CYYT", // St. John's - down the eastern seaboard
        "KLEB", // New England Mountains
        "TJSJ", // San Juan - Caribbean
        "SKBO", // Bogota - into South America
        "SPZO", // Parque Fernando / Peru Reserve (Cusco)
        "SCEL", // Santiago - toward the Antarctica gateway
        "SAEZ", // Buenos Aires - back up through South America
        "MPTO", // Panama - Central America
        "MMCU", // Rancho del Arroyo (Chihuahua)
        "KJAN", // Mississippi Acres Preserve
        AirportSeedImporter.HomeIcao, // KVGT - return
    ];

    /// <summary>Inserted right after "SCEL" for the Chile Peninsula gateway - a short Drake Passage hop, not a trek to the Ross Sea side.</summary>
    private static readonly string[] ChilePeninsulaSegment = ["SCGZ", "SCRM"];

    /// <summary>Inserted right after "NZRO" for the New Zealand/McMurdo gateway - Christchurch is the real-world McMurdo gateway.</summary>
    private static readonly string[] NewZealandMcMurdoSegment = ["NZCH", "NZWD"];

    private static string[] BuildIcaoOrder(AntarcticaGateway gateway, bool includeReserveWaypoints)
    {
        var order = new List<string>(BaseIcaoOrder);
        var (afterIcao, segment) = gateway switch
        {
            AntarcticaGateway.NewZealandMcMurdo => ("NZRO", NewZealandMcMurdoSegment),
            _ => ("SCEL", ChilePeninsulaSegment),
        };

        var insertAt = order.IndexOf(afterIcao) + 1;
        order.InsertRange(insertAt, segment);

        // The reserve stand-ins are woven into the spine as regular narrative anchors (Denver, Cairns,
        // Rovaniemi, etc.) - dropping them when the theme is unwanted just means the westbound flow
        // skips straight past that theme stop to the next spine waypoint; leg-splitting still finds
        // whatever real intermediate stops that stretch actually needs, same as any other gap.
        if (!includeReserveWaypoints)
        {
            order.RemoveAll(icao => AirportSeedImporter.ReserveWaypoints.ContainsKey(icao));
        }

        return [.. order];
    }

    public static Itinerary CreateDefaultWestboundItinerary(
        FlightPlannerDbContext context, Aircraft aircraft, int countryGoalPercent = 50, double maxFlightTimeHours = 3,
        AntarcticaGateway antarcticaGateway = AntarcticaGateway.ChilePeninsula, bool includeReserveWaypoints = true)
    {
        var airportsByIcao = context.Airports.ToDictionary(a => a.Icao);
        var icaoOrder = BuildIcaoOrder(antarcticaGateway, includeReserveWaypoints);

        var itinerary = new Itinerary
        {
            Name = "Round-the-World Westbound",
            CreatedUtc = DateTime.UtcNow,
            AircraftId = aircraft.Id,
            CountryGoalPercent = countryGoalPercent,
            MaxFlightTimeHours = maxFlightTimeHours,
        };

        for (var i = 0; i < icaoOrder.Length - 1; i++)
        {
            var from = airportsByIcao[icaoOrder[i]];
            var to = airportsByIcao[icaoOrder[i + 1]];
            var distanceNm = GreatCircleMath.DistanceNm(from, to);

            itinerary.Legs.Add(new FlightLeg
            {
                SequenceNumber = i + 1,
                DepartureAirportId = from.Id,
                ArrivalAirportId = to.Id,
                PlannedCruiseSpeedKts = aircraft.CruiseSpeedKts,
                PlannedDistanceNm = distanceNm,
                IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(from, to, distanceNm),
                IsSpineAnchor = true,
            });
        }

        context.Itineraries.Add(itinerary);
        context.SaveChanges();
        return itinerary;
    }
}
