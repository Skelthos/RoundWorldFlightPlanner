using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Splits any leg that's actually too long into real intermediate stops - rather than just showing
/// a "Warning" and leaving it as a single unflyable hop. The tighter max-flight-time cap is always
/// tried first, even for a leg flagged IsOceanCrossing - "exempt from the time warning" was meant for
/// genuine nowhere-to-stop crossings, not a license to skip looking for closer islands/stops that
/// would keep the hop short. Only when no stepping stone exists within the tight cap does a crossing
/// fall back to the looser realistic-fuel-range cap. Genuine long crossings with no reachable
/// stepping stone at either cap (e.g. mid-Pacific, Antarctica) are left as-is - there's nowhere to
/// put a fuel stop.
/// </summary>
public class LegSplittingService(IRouteDistanceService distanceService)
{
    /// <summary>Fraction of theoretical zero-reserve range treated as usable, to leave a realistic fuel reserve.</summary>
    private const double RangeSafetyMargin = 0.85;
    private const int MaxHopsPerLeg = 12;

    public void SplitOverlongLegs(Itinerary itinerary, IReadOnlyList<Airport> allAirports)
    {
        var aircraft = itinerary.Aircraft;
        if (aircraft is null)
        {
            return;
        }

        var maxTimeDistanceNm = aircraft.CruiseSpeedKts * itinerary.MaxFlightTimeHours;
        var maxRangeNm = aircraft.FuelBurnKgPerHour > 0
            ? aircraft.FuelCapacityKg / aircraft.FuelBurnKgPerHour * aircraft.CruiseSpeedKts * RangeSafetyMargin
            : maxTimeDistanceNm;

        var orderedLegs = itinerary.Legs.OrderBy(l => l.SequenceNumber).ToList();

        // Soft preference, not a hard rule: reusing an airport as a stepping stone for more than one
        // leg is a bit repetitive on the map, so each chain prefers fresh airports first - but this
        // must never BLOCK a pick outright, or a sparse region starves later legs of stepping stones
        // entirely (the original bug). Updated as each chain is built so later legs also prefer
        // avoiding what earlier legs just used.
        var preferFreshOverIcaos = orderedLegs
            .SelectMany(l => new[] { l.DepartureAirport!.Icao, l.ArrivalAirport!.Icao })
            .ToHashSet();

        // Same idea, one level up: a country already counted toward some continent's coverage goal
        // shouldn't casually get reused as a stepping stone for a totally unrelated split elsewhere,
        // when a fresh-country candidate exists in the corridor - a sparse archipelago (the South
        // Pacific especially) can otherwise have the same handful of small nations picked as "the
        // nearest thing" for several unrelated splits, each via a different one of that country's
        // airports, which reads as bouncing back and forth between the same few countries even though
        // no single chain repeats anything itself.
        var preferFreshOverCountries = orderedLegs
            .SelectMany(l => new[] { l.DepartureAirport!.Country, l.ArrivalAirport!.Country })
            .ToHashSet();

        var strictCapNm = Math.Min(maxTimeDistanceNm, maxRangeNm);

        foreach (var leg in orderedLegs.ToList())
        {
            if (leg.IsComplete || leg.Phase != Core.Enums.FlightPhase.NotStarted)
            {
                continue; // never rewrite a leg that has been flown or is in progress
            }

            if (leg.PlannedDistanceNm <= strictCapNm)
            {
                continue; // already fine even under the tighter cap - nothing to look for
            }

            // Always try the tight (max-flight-time) cap first, regardless of the ocean-crossing flag -
            // real stepping stones (islands, coastal strips) often mean it isn't actually a nowhere-to-
            // stop crossing. Only fall back to the looser realistic-range cap if that fails AND this
            // leg is a genuine crossing that's still too long even at the loose cap.
            var stops = BuildChain(leg.DepartureAirport!, leg.ArrivalAirport!, strictCapNm, allAirports, preferFreshOverIcaos, preferFreshOverCountries);

            // BuildChain can run out of reachable stepping stones partway through (e.g. departing from
            // a genuinely isolated island with nothing else within the cap) and still return a non-empty
            // chain - it just means the LAST hop, from wherever it got stuck to the true destination, is
            // still far over the cap. Checking `stops.Count <= 1` alone misses this: a chain that found
            // one real intermediate stop but then stalled looks "non-empty" even though the leg it
            // produces is exactly as unflyable as if nothing had been found at all - confirmed via a real
            // case (PHTO -> VABB, Hawaii to Mumbai) where the strict-cap chain found only Midway before
            // running out of anything reachable further west, leaving a final Midway -> Mumbai hop at
            // 5,841nm - undetected by the old check since the chain wasn't literally empty.
            var lastHopDistanceNm = stops.Count >= 2
                ? distanceService.GreatCircleDistanceNm(stops[^2], stops[^1])
                : distanceService.GreatCircleDistanceNm(leg.DepartureAirport!, stops[0]);

            if (lastHopDistanceNm > strictCapNm && leg.IsOceanCrossing && leg.PlannedDistanceNm > maxRangeNm)
            {
                stops = BuildChain(leg.DepartureAirport!, leg.ArrivalAirport!, maxRangeNm, allAirports, preferFreshOverIcaos, preferFreshOverCountries);
            }

            if (stops.Count <= 1)
            {
                continue; // no reachable stepping stone found at any applicable cap - leave the long hop as-is
            }

            var insertIndex = orderedLegs.IndexOf(leg);
            orderedLegs.RemoveAt(insertIndex);
            itinerary.Legs.Remove(leg);

            var current = leg.DepartureAirport!;
            var newLegs = new List<FlightLeg>();
            foreach (var stop in stops)
            {
                var distanceNm = distanceService.GreatCircleDistanceNm(current, stop);
                var newLeg = new FlightLeg
                {
                    ItineraryId = itinerary.Id,
                    DepartureAirportId = current.Id,
                    DepartureAirport = current,
                    ArrivalAirportId = stop.Id,
                    ArrivalAirport = stop,
                    PlannedCruiseSpeedKts = aircraft.CruiseSpeedKts,
                    PlannedDistanceNm = distanceNm,
                    IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(current, stop, distanceNm),
                };
                newLegs.Add(newLeg);
                itinerary.Legs.Add(newLeg);
                preferFreshOverIcaos.Add(stop.Icao);
                preferFreshOverCountries.Add(stop.Country);
                current = stop;
            }

            // The final new leg lands at the original leg's true destination - if that was a spine
            // waypoint, the flag has to carry over, or a later "Refactor Route" would treat this
            // narrative anchor as disposable filler and delete it.
            if (leg.IsSpineAnchor)
            {
                newLegs[^1].IsSpineAnchor = true;
            }

            orderedLegs.InsertRange(insertIndex, newLegs);
        }

        for (var i = 0; i < orderedLegs.Count; i++)
        {
            orderedLegs[i].SequenceNumber = i + 1;
        }
    }

    /// <summary>Minimum half-width of the "stay near the direct line" corridor, in nm - keeps very short overlong legs from being overly restrictive.</summary>
    private const double MinCorridorHalfWidthNm = 250;

    /// <summary>Corridor half-width as a fraction of the total leg distance being split.</summary>
    private const double CorridorHalfWidthFraction = 0.15;

    /// <summary>
    /// Steps from <paramref name="from"/> toward <paramref name="to"/>. Each hop maximizes distance
    /// covered (fewest, biggest compliant jumps) among candidates that stay within a corridor around
    /// the direct great-circle line - filtering by corridor first, THEN maximizing distance, avoids
    /// two failure modes: sorting purely by distance detours toward whatever's farthest regardless of
    /// direction (e.g. Ontario -&gt; Newfoundland -&gt; Pennsylvania zigzags); sorting purely by
    /// cross-track deviation instead picks whatever's most precisely on the line even if it's only a
    /// few nm away, wasting hops nibbling at near-zero progress. Candidates are ranked by a composite
    /// score (same continent as either endpoint &gt; fresh country &gt; fresh airport &gt; within the
    /// corridor), falling through to worse tiers only when a better one has no candidates at all -
    /// none of these are hard rules, since a sparse region with nothing better available must never be
    /// left unsplit. The same-continent preference specifically guards against a real-world quirk: near
    /// the poles, a pure "shortest great-circle line" search can wander through a completely different
    /// continent's airports (e.g. bridging two European stops via Alaska/Arctic Canada) since a great
    /// circle between two high-latitude points curves through surprising longitudes - mathematically
    /// the shortest path, but nonsensical as a flight plan. The fresh-country preference guards against
    /// a similar quirk in sparse archipelagos (the South Pacific especially): without it, several
    /// unrelated splits can each independently reach for "the nearest thing," which in a region with
    /// only a handful of small nations often means a different airport of the *same* already-counted
    /// country each time - visually indistinguishable from genuine back-and-forth. Always ends with
    /// <paramref name="to"/>.
    /// </summary>
    private List<Airport> BuildChain(Airport from, Airport to, double capNm, IReadOnlyList<Airport> allAirports, HashSet<string> preferFreshOverIcaos, HashSet<string> preferFreshOverCountries)
    {
        var chain = new List<Airport>();
        var current = from;
        var hops = 0;
        var usedInThisChain = new HashSet<string> { from.Icao, to.Icao };
        var corridorHalfWidthNm = Math.Max(MinCorridorHalfWidthNm, distanceService.GreatCircleDistanceNm(from, to) * CorridorHalfWidthFraction);

        while (distanceService.GreatCircleDistanceNm(current, to) > capNm && hops++ < MaxHopsPerLeg)
        {
            var remainingToDestination = distanceService.GreatCircleDistanceNm(current, to);

            var candidates = allAirports
                .Where(a => !usedInThisChain.Contains(a.Icao))
                .Select(a => (
                    Airport: a,
                    FromCurrentNm: distanceService.GreatCircleDistanceNm(current, a),
                    ToDestinationNm: distanceService.GreatCircleDistanceNm(a, to),
                    CrossTrackNm: GreatCircleMath.CrossTrackDistanceNm(from, to, a),
                    IsFreshCountry: !preferFreshOverCountries.Contains(a.Country),
                    IsFresh: !preferFreshOverIcaos.Contains(a.Icao),
                    IsOnContinent: a.ContinentCode == from.ContinentCode || a.ContinentCode == to.ContinentCode))
                .Where(c => c.FromCurrentNm <= capNm && c.ToDestinationNm < remainingToDestination)
                .ToList();

            var best = candidates
                .OrderByDescending(c => (c.IsOnContinent ? 8 : 0) + (c.IsFreshCountry ? 4 : 0) + (c.IsFresh ? 2 : 0) + (c.CrossTrackNm <= corridorHalfWidthNm ? 1 : 0))
                .ThenByDescending(c => c.FromCurrentNm)
                .Select(c => c.Airport)
                .FirstOrDefault();

            if (best is null)
            {
                break; // no reachable stepping stone makes progress - stop here, leave the remainder as one (possibly long) final hop
            }

            chain.Add(best);
            usedInThisChain.Add(best.Icao);
            preferFreshOverIcaos.Add(best.Icao);
            preferFreshOverCountries.Add(best.Country);
            current = best;
        }

        chain.Add(to);
        return chain;
    }
}
