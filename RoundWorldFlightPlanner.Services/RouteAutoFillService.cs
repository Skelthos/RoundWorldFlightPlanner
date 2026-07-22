using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// After the narrative spine is built, inserts additional in-continent stops so the itinerary
/// actually reaches the country-coverage goal per continent - rather than leaving 20+ manual
/// "go add a leg" clicks per continent to the pilot. A continent the spine visits more than once
/// (e.g. North America at trip start, again around Greenland/New England, again near the end) gets
/// its needed filler split evenly across each visit rather than all crammed into the last one -
/// dumping everything into a single spot produces a dense, backtracking-looking tangle on the map
/// even when each individual chain is well-ordered. Within each visit, candidates are ordered by
/// <see cref="BuildCoherentSweepOrder"/> - a constructive, non-crossing walk (a single directional
/// sweep, or an out-along-one-side/back-along-the-other loop when the visit needs to double back
/// toward where it entered) rather than an angular sweep plus a 2-opt distance-minimizing cleanup.
/// The pilot's stated priority is a flowing, coherent route where most hops are under the time cap -
/// hop count and total distance are explicitly not a constraint - and direct hand-verification showed
/// 2-opt's distance-minimizing objective actively fights that goal (it was already finding the
/// shortest-distance tour for a visually "weaving" case, so making it try harder wasn't the fix -
/// the objective itself was wrong). Antarctica is exempt (one landing is already enough) and the
/// final return-to-home leg is never used as an anchor.
/// </summary>
public class RouteAutoFillService(IRouteDistanceService distanceService)
{
    private const string AntarcticaCode = "AN";

    /// <summary>
    /// Strips every not-yet-flown, non-spine leg (prior country-fill stops and split-in fuel stops)
    /// back out, repairing continuity between whatever legs survive - flown history and the original
    /// named-waypoint spine are untouched. Call this before re-running <see cref="FillToCountryGoal"/>
    /// and a leg-splitting pass so "Refactor Route" actually regenerates the auto-generated portions
    /// with the current algorithm, instead of only topping up a route that's already technically
    /// compliant (and so has nothing left for those two passes to find).
    /// </summary>
    public void ResetToSpine(Itinerary itinerary)
    {
        var orderedLegs = itinerary.Legs.OrderBy(l => l.SequenceNumber).ToList();

        var toRemove = orderedLegs.Where(l => !l.IsComplete && !l.IsSpineAnchor).ToList();
        foreach (var leg in toRemove)
        {
            itinerary.Legs.Remove(leg);
            orderedLegs.Remove(leg);
        }

        for (var i = 1; i < orderedLegs.Count; i++)
        {
            var previousArrival = orderedLegs[i - 1].ArrivalAirport!;
            var leg = orderedLegs[i];
            if (leg.IsComplete || leg.DepartureAirportId == previousArrival.Id)
            {
                continue;
            }

            leg.DepartureAirportId = previousArrival.Id;
            leg.DepartureAirport = previousArrival;
            leg.PlannedDistanceNm = distanceService.GreatCircleDistanceNm(previousArrival, leg.ArrivalAirport!);
            leg.IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(previousArrival, leg.ArrivalAirport!, leg.PlannedDistanceNm);
        }

        for (var i = 0; i < orderedLegs.Count; i++)
        {
            orderedLegs[i].SequenceNumber = i + 1;
        }
    }

    public void FillToCountryGoal(Itinerary itinerary, IReadOnlyList<Airport> allAirports)
    {
        var orderedLegs = itinerary.Legs.OrderBy(l => l.SequenceNumber).ToList();
        if (orderedLegs.Count < 2)
        {
            return;
        }

        var reference = ContinentCountryReference.CountriesByContinent;
        var usedIcaos = orderedLegs
            .SelectMany(l => new[] { l.DepartureAirport!.Icao, l.ArrivalAirport!.Icao })
            .ToHashSet();

        // The final leg lands back home - never a valid anchor to build filler off of, the trip is over by then.
        var anchorCandidates = orderedLegs.Take(orderedLegs.Count - 1).ToList();
        var continentsPresent = anchorCandidates
            .Select(l => l.ArrivalAirport!.ContinentCode)
            .Distinct()
            .Where(c => c != AntarcticaCode);

        foreach (var continent in continentsPresent)
        {
            var totalCountries = reference.GetValueOrDefault(continent, new HashSet<string>()).Count;
            if (totalCountries == 0)
            {
                continue;
            }

            var targetCount = (int)Math.Ceiling(totalCountries * itinerary.CountryGoalPercent / 100.0);
            var validCountries = reference[continent];

            // Only countries that actually count toward the goal (sovereign nations, per
            // ContinentCountryReference) are tracked as "visited" here - a spine stop at a territory
            // like Bermuda or Greenland still gets flown, it just doesn't consume part of the goal or
            // block a real country from being picked later.
            var visitedCountries = orderedLegs
                .Where(l => l.ArrivalAirport!.ContinentCode == continent && validCountries.Contains(l.ArrivalAirport!.Country))
                .Select(l => l.ArrivalAirport!.Country)
                .ToHashSet();
            if (orderedLegs[0].DepartureAirport!.ContinentCode == continent && validCountries.Contains(orderedLegs[0].DepartureAirport!.Country))
            {
                visitedCountries.Add(orderedLegs[0].DepartureAirport!.Country);
            }

            var shortfall = targetCount - visitedCountries.Count;
            if (shortfall <= 0)
            {
                continue;
            }

            // Every individual spine leg arriving in this continent is its own potential insertion point
            // - not just the last leg of each maximal contiguous visit. Restricting insertion to only
            // the tail of a visit meant a country that's actually closest to an EARLIER waypoint (e.g.
            // Mongolia/Russia/Korea, right next to Japan) could only ever be tacked onto the very end of
            // the visit instead (e.g. after Singapore, thousands of miles further south) - forcing a
            // huge "descend, then climb all the way back north, then descend again" loop that was
            // entirely an artifact of where insertion was allowed, not of the countries' real geography.
            // Grouped by VISIT (maximal contiguous stretch) rather than flattened, because the fair-share
            // math still needs to operate at that level: dividing the shortfall evenly across every
            // individual anchor (potentially a dozen-plus once every spine leg counts) badly under-shares
            // continents with many visits, since most anchors only have a small, geography-scoped bucket
            // and can't use a large "fair share" anyway - confirmed as a real regression (Europe/North
            // America both dropped below goal) before this two-level split was added.
            var visits = GroupAnchorsByVisit(anchorCandidates, continent)
                .Select(visit => visit.Where(a => orderedLegs.FirstOrDefault(l => l.SequenceNumber == a.SequenceNumber + 1) is not { IsComplete: true }).ToList())
                .Where(visit => visit.Count > 0)
                .ToList();
            if (visits.Count == 0)
            {
                continue;
            }

            var runs = visits.SelectMany(v => v).ToList();

            // Restricted to sovereign-nation airports up front - a territory (Bermuda, Greenland, Hong
            // Kong, etc.) doesn't count toward the goal at all, so there's no reason for the fill to ever
            // spend one of its picks landing at one; it would look like progress without actually being
            // any (see the `validCountries` filter on `visitedCountries` above for the matching half of
            // this fix).
            var continentAirports = allAirports.Where(a => a.ContinentCode == continent && validCountries.Contains(a.Country)).ToList();

            // When a continent is visited more than once (e.g. Asia near Japan on the way out, then
            // again near Dubai/Mumbai on the way to Africa), each anchor's candidate pool is restricted to
            // airports that genuinely belong to THAT anchor's own path, not just whichever anchor's
            // POINT happens to be nearest. Nearest-anchor-point alone isn't enough: a candidate can be
            // the closest point to one anchor while still being a real detour off that anchor's actual
            // direction of travel, when it would have been directly "on the way" for a different anchor
            // instead (confirmed: with Alaska's reserve stop skipped, an early California-anchored run
            // picked a Baja California Mexico airport - genuinely the closest anchor point - even though
            // that run's very next leg was a giant jump toward Tokyo, nowhere near Mexico, while the LATER
            // run already flies Panama -> north through Mexico -> home, so any Mexican airport is
            // free/on-the-way there). Fixed by partitioning on distance to each anchor's own
            // anchor-to-reconnect LINE (cross-track distance), not distance to the anchor point.
            var reconnectTargets = runs
                .Select(run => orderedLegs.FirstOrDefault(l => l.SequenceNumber == run.SequenceNumber + 1)?.ArrivalAirport)
                .ToList();
            var airportsByRun = runs.Count > 1
                ? PartitionByNearestRunPath(continentAirports, runs, reconnectTargets)
                : [continentAirports];

            var runIndex = 0;
            for (var visitIndex = 0; visitIndex < visits.Count && shortfall > 0; visitIndex++)
            {
                var visitsRemainingAfterThis = visits.Count - visitIndex - 1;
                var shareForThisVisit = visitsRemainingAfterThis == 0
                    ? shortfall
                    : (int)Math.Ceiling((double)shortfall / (visitsRemainingAfterThis + 1));

                // Within a visit, each internal anchor can take as much of the visit's own share as its
                // (already geography-restricted) bucket supports - no further even split needed here,
                // since PartitionByNearestRunPath already scopes each anchor to only what's genuinely on
                // its own path.
                var visitShortfall = shareForThisVisit;
                foreach (var _ in visits[visitIndex])
                {
                    var addedCount = FillOneRun(itinerary, orderedLegs, runs[runIndex], airportsByRun[runIndex], Math.Max(0, visitShortfall), visitedCountries, usedIcaos);
                    visitShortfall -= addedCount;
                    shortfall -= addedCount;
                    runIndex++;
                }
            }
        }

        for (var i = 0; i < orderedLegs.Count; i++)
        {
            orderedLegs[i].SequenceNumber = i + 1;
        }
    }

    /// <summary>Fills up to <paramref name="countryTarget"/> new countries starting from one visit's anchor leg. Returns how many new countries were actually added.</summary>
    private int FillOneRun(
        Itinerary itinerary,
        List<FlightLeg> orderedLegs,
        FlightLeg runAnchorLeg,
        IReadOnlyList<Airport> continentAirports,
        int countryTarget,
        HashSet<string> visitedCountries,
        HashSet<string> usedIcaos)
    {
        var anchor = runAnchorLeg.ArrivalAirport!;

        // Known up front so the optimizer can plan the whole chain toward it, not just anchor outward -
        // otherwise an internally-efficient tour can still end up far from where it needs to reconnect,
        // forcing a huge bridging detour back (this was the actual cause of the worst-looking tangles).
        var followingLeg = orderedLegs.FirstOrDefault(l => l.SequenceNumber == runAnchorLeg.SequenceNumber + 1);
        var reconnectTarget = followingLeg?.ArrivalAirport;

        // A country spanning a huge area (Russia above all, but also China/Canada/USA/Brazil/Australia)
        // can have candidate airports thousands of miles apart within the same bucket - the walk-and-
        // pick-first-unclaimed-country selection below only cares WHICH country a candidate belongs to,
        // so without this it can just as easily grab the single most extreme, far-flung outlier as a
        // sensible central one (confirmed: an Asia run anchored at Tokyo reached all the way to
        // Provideniya Bay, right at the Bering Strait next to Alaska, for "Russia," then had to jump
        // straight back to Mongolia - a needless detour when far-nearer Russian Far East cities like
        // Vladivostok exist). Collapsing to the single closest-to-this-anchor's-path airport per country
        // up front means the walk can now only ever pick the best available representative for each
        // country, not an arbitrary one.
        var bestAirportPerCountry = continentAirports
            .GroupBy(a => a.Country)
            .Select(g => g.OrderBy(a => DistanceToRunPathNm(anchor, reconnectTarget, a)).First())
            .ToList();

        var sweepOrder = BuildCoherentSweepOrder(anchor, reconnectTarget, bestAirportPerCountry);

        var selectedStops = new List<Airport>();
        foreach (var candidateAirport in sweepOrder)
        {
            if (selectedStops.Count >= countryTarget)
            {
                break;
            }

            if (usedIcaos.Contains(candidateAirport.Icao) || visitedCountries.Contains(candidateAirport.Country))
            {
                continue;
            }

            selectedStops.Add(candidateAirport);
            visitedCountries.Add(candidateAirport.Country);
            usedIcaos.Add(candidateAirport.Icao);
        }

        if (selectedStops.Count == 0)
        {
            return 0;
        }

        // The constructive sweep's single global bearing estimate is a good starting shape, but a real,
        // unevenly-spread set of countries rarely fits its two-lane model perfectly - this cleans up
        // whatever it got wrong, optimizing directly for fewer sharp turns (not shorter distance).
        CoherenceCleanup(anchor, selectedStops, reconnectTarget);

        var currentPosition = anchor;
        var newLegs = new List<FlightLeg>();
        foreach (var stop in selectedStops)
        {
            var distanceNm = distanceService.GreatCircleDistanceNm(currentPosition, stop);
            newLegs.Add(new FlightLeg
            {
                ItineraryId = itinerary.Id,
                DepartureAirportId = currentPosition.Id,
                DepartureAirport = currentPosition,
                ArrivalAirportId = stop.Id,
                ArrivalAirport = stop,
                PlannedCruiseSpeedKts = itinerary.Aircraft?.CruiseSpeedKts ?? 0,
                PlannedDistanceNm = distanceNm,
                IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(currentPosition, stop, distanceNm),
            });

            currentPosition = stop;
        }

        // Reconnect: whatever leg used to depart from this run's anchor now departs from wherever the filler chain ended up.
        if (followingLeg is not null)
        {
            followingLeg.DepartureAirportId = currentPosition.Id;
            followingLeg.DepartureAirport = currentPosition;
            followingLeg.PlannedDistanceNm = distanceService.GreatCircleDistanceNm(currentPosition, followingLeg.ArrivalAirport!);
            followingLeg.IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(currentPosition, followingLeg.ArrivalAirport!, followingLeg.PlannedDistanceNm);
        }

        var insertIndex = orderedLegs.FindIndex(l => l.SequenceNumber == runAnchorLeg.SequenceNumber) + 1;
        orderedLegs.InsertRange(insertIndex, newLegs);
        itinerary.Legs.AddRange(newLegs);

        return selectedStops.Count;
    }

    /// <summary>
    /// Groups <paramref name="anchorCandidates"/> into maximal contiguous visits to <paramref name="continent"/>
    /// - every leg within one unbroken stretch of arrivals in that continent forms one visit's group,
    /// each entry in the group being its own potential insertion anchor (see the fill-loop comment for
    /// why every leg, not just the last one, matters). A new visit starts whenever a leg arriving in a
    /// different continent breaks the stretch.
    /// </summary>
    private static List<List<FlightLeg>> GroupAnchorsByVisit(List<FlightLeg> anchorCandidates, string continent)
    {
        var visits = new List<List<FlightLeg>>();
        List<FlightLeg>? current = null;

        foreach (var leg in anchorCandidates)
        {
            if (leg.ArrivalAirport!.ContinentCode == continent)
            {
                current ??= [];
                current.Add(leg);
            }
            else if (current is not null)
            {
                visits.Add(current);
                current = null;
            }
        }

        if (current is not null)
        {
            visits.Add(current);
        }

        return visits;
    }

    /// <summary>
    /// Buckets <paramref name="continentAirports"/> by whichever visit's own anchor-to-reconnect path
    /// passes closest, so each visit only ever considers airports genuinely on its own path - not just
    /// whichever visit's anchor POINT happens to be nearest, which can still be a real detour if that
    /// visit's next leg heads somewhere else entirely. Falls back to distance-to-anchor-point for any
    /// visit with no reconnect leg to measure against (shouldn't normally happen, since every run but
    /// the truly final one has a following leg).
    /// </summary>
    private List<List<Airport>> PartitionByNearestRunPath(List<Airport> continentAirports, List<FlightLeg> runs, List<Airport?> reconnectTargets)
    {
        var anchors = runs.Select(r => r.ArrivalAirport!).ToList();
        var buckets = anchors.Select(_ => new List<Airport>()).ToList();

        foreach (var airport in continentAirports)
        {
            var nearestRunIndex = 0;
            var nearestDistanceNm = double.MaxValue;
            for (var i = 0; i < anchors.Count; i++)
            {
                var distanceNm = DistanceToRunPathNm(anchors[i], reconnectTargets[i], airport);
                if (distanceNm < nearestDistanceNm)
                {
                    nearestDistanceNm = distanceNm;
                    nearestRunIndex = i;
                }
            }

            buckets[nearestRunIndex].Add(airport);
        }

        return buckets;
    }

    /// <summary>
    /// How much LONGER <paramref name="anchor"/>'s own path to <paramref name="reconnect"/> becomes if it
    /// detours through <paramref name="point"/> - the classic "cheapest insertion" TSP measure, and a much
    /// more direct answer to "does this candidate belong to this anchor" than distance alone. Distance-
    /// based scoring (cross-track to the anchor-reconnect line, or plain distance-to-anchor as a fallback)
    /// was tried and found wanting twice over: it can't tell a genuinely short hop (e.g. Darwin's own next
    /// stop is just Cairns, a few hundred nm away) from one that's merely closest in absolute terms -
    /// confirmed directly: Pacific islands (Papua New Guinea, Solomon Islands, New Caledonia) scored as
    /// "closer to Darwin" than to any other anchor purely by raw distance, so Darwin's short Cairns hop
    /// got saddled with a huge Pacific detour before the Australia circumnavigation had even started,
    /// forcing a jump all the way back to Tasmania afterward and a second reach back out to the same
    /// islands later - a real "weird circle" on the map. Insertion cost fixes this directly: detouring a
    /// short hop through a distant candidate is expensive relative to that hop's own size, so it correctly
    /// loses to whichever anchor's own path is already headed that direction (e.g. Perth's crossing to
    /// New Zealand, which the Pacific islands are much more genuinely "on the way" for).
    /// </summary>
    private double DistanceToRunPathNm(Airport anchor, Airport? reconnect, Airport point)
    {
        if (reconnect is null)
        {
            return distanceService.GreatCircleDistanceNm(anchor, point);
        }

        var directNm = distanceService.GreatCircleDistanceNm(anchor, reconnect);
        var viaPointNm = distanceService.GreatCircleDistanceNm(anchor, point) + distanceService.GreatCircleDistanceNm(point, reconnect);
        return viaPointNm - directNm;
    }

    /// <summary>
    /// Orders a continent visit's candidates for a coherent, non-crossing walk - the pilot's own stated
    /// priority is a flowing route with every hop under the time cap, not the fewest hops or the
    /// shortest total distance (confirmed: distance-minimizing 2-opt/or-opt was previously proven, by
    /// direct hand-verification, to already be near-optimal by distance for Africa's messiest-looking
    /// case - the "weaving" was real geography, not a solver bug - so minimizing distance further is the
    /// wrong objective entirely, not just an unsolved one).
    /// <para>
    /// Two shapes, chosen by comparing the bearing from the anchor toward the candidates as a whole
    /// against the bearing from the anchor toward <paramref name="reconnectTarget"/>:
    /// </para>
    /// <para>
    /// <b>Through</b> (the two bearings roughly agree, e.g. Tokyo -&gt; Southeast Asia -&gt; Darwin,
    /// continuing the same general direction): a single sweep ordered by how far each candidate
    /// projects along that direction, nearest-to-anchor first - one continuous pass toward the
    /// reconnect point, never doubling back over ground already covered.
    /// </para>
    /// <para>
    /// <b>Loop</b> (the two bearings roughly oppose, e.g. Morocco -&gt; the rest of Africa -&gt; back up
    /// near Spain: the candidates are all in the opposite direction from where the trip needs to end up
    /// next): candidates are split into two lanes by which side of the outbound line they fall on, one
    /// lane walked outbound (near to far) and the other walked back (far to near) - out along one side
    /// of the region, back along the other, the way you'd actually plan a there-and-back trip by hand
    /// rather than crossing your own path partway through.
    /// </para>
    /// </summary>
    private List<Airport> BuildCoherentSweepOrder(Airport anchor, Airport? reconnectTarget, IReadOnlyList<Airport> continentAirports)
    {
        if (continentAirports.Count == 0)
        {
            return [];
        }

        var centroid = new Airport
        {
            Icao = string.Empty,
            Name = string.Empty,
            City = string.Empty,
            Country = string.Empty,
            ContinentCode = string.Empty,
            Latitude = continentAirports.Average(a => a.Latitude),
            Longitude = continentAirports.Average(a => a.Longitude),
        };

        var outwardBearingDeg = GreatCircleMath.InitialBearingDeg(anchor, centroid);
        var reconnectBearingDeg = reconnectTarget is not null
            ? GreatCircleMath.InitialBearingDeg(anchor, reconnectTarget)
            : outwardBearingDeg;

        var isLoop = Math.Abs(GreatCircleMath.NormalizeLongitudeDelta(reconnectBearingDeg - outwardBearingDeg)) > 90;
        var sweepBearingDeg = isLoop ? outwardBearingDeg : reconnectBearingDeg;

        var projected = continentAirports.Select(a =>
        {
            var distanceNm = distanceService.GreatCircleDistanceNm(anchor, a);
            var relativeBearingRad = GreatCircleMath.NormalizeLongitudeDelta(GreatCircleMath.InitialBearingDeg(anchor, a) - sweepBearingDeg) * Math.PI / 180.0;
            return (Airport: a, AlongTrackNm: distanceNm * Math.Cos(relativeBearingRad), IsRightOfSweep: Math.Sin(relativeBearingRad) >= 0);
        });

        if (!isLoop)
        {
            return projected.OrderBy(p => p.AlongTrackNm).Select(p => p.Airport).ToList();
        }

        var outboundLane = projected.Where(p => p.IsRightOfSweep).OrderBy(p => p.AlongTrackNm).Select(p => p.Airport);
        var returnLane = projected.Where(p => !p.IsRightOfSweep).OrderByDescending(p => p.AlongTrackNm).Select(p => p.Airport);
        return outboundLane.Concat(returnLane).ToList();
    }

    /// <summary>
    /// Cleans up whatever <see cref="BuildCoherentSweepOrder"/>'s single global bearing estimate got
    /// slightly wrong - a real, sparse country distribution rarely sits neatly in two clean lanes, and
    /// with no cleanup step the construction's mistakes go straight onto the map. Reuses the shape of
    /// classic 2-opt (segment reversal) and or-opt (single-point relocation), but the accept/reject
    /// decision is <b>sharp-turn count first, total distance only as a tie-break</b> - the inverse of
    /// the classic distance-first criterion. Distance-first was tried and rejected: it already converged
    /// to a near-optimal-distance tour for the flagged Africa case that still "wove" visually, proving
    /// distance was never the right thing to minimize once hop count stopped being a constraint - fewer
    /// sharp turns is the actual, stated goal, so that's what this optimizes for directly.
    /// </summary>
    private void CoherenceCleanup(Airport anchor, List<Airport> stops, Airport? fixedEnd)
    {
        var path = new List<Airport> { anchor };
        path.AddRange(stops);
        if (fixedEnd is not null)
        {
            path.Add(fixedEnd);
        }

        if (path.Count < 4)
        {
            return;
        }

        const int maxRounds = 25;
        var round = 0;
        bool anyImproved;

        do
        {
            anyImproved = CoherenceTwoOptPass(path);
            anyImproved |= CoherenceOrOptPass(path);
        }
        while (anyImproved && round++ < maxRounds);

        stops.Clear();
        var stopsCount = fixedEnd is not null ? path.Count - 2 : path.Count - 1;
        stops.AddRange(path.Skip(1).Take(stopsCount));
    }

    /// <summary>
    /// (crossing count, sharp-turn count, total distance) for a path - in that priority order. Sharp-turn
    /// count alone (the original version of this cleanup) only ever catches a bad LOCAL turn between two
    /// consecutive legs - it can't see two non-adjacent legs' lines actually crossing each other on the
    /// map, which is what "criss-crossing" concretely means and turned out to still be common even after
    /// the sharp-turn fix (confirmed from real map screenshots showing an X/star pattern among legs that
    /// individually had no sharp turn at either end). Crossing count is now the primary objective; turn
    /// count and distance only break ties among equally-uncrossed candidates.
    /// </summary>
    private (int Crossings, int SharpTurns, double TotalDistanceNm) EvaluatePath(List<Airport> path)
    {
        double totalDistanceNm = 0;
        double? previousBearingDeg = null;
        var sharpTurns = 0;

        for (var i = 0; i < path.Count - 1; i++)
        {
            var bearingDeg = GreatCircleMath.InitialBearingDeg(path[i], path[i + 1]);
            totalDistanceNm += distanceService.GreatCircleDistanceNm(path[i], path[i + 1]);

            if (previousBearingDeg.HasValue)
            {
                var turnDeg = Math.Abs(GreatCircleMath.NormalizeLongitudeDelta(bearingDeg - previousBearingDeg.Value));
                if (turnDeg > 120)
                {
                    sharpTurns++;
                }
            }

            previousBearingDeg = bearingDeg;
        }

        var crossings = 0;
        for (var i = 0; i < path.Count - 1; i++)
        {
            for (var j = i + 2; j < path.Count - 1; j++)
            {
                if (SegmentsIntersect(path[i], path[i + 1], path[j], path[j + 1]))
                {
                    crossings++;
                }
            }
        }

        return (crossings, sharpTurns, totalDistanceNm);
    }

    /// <summary>
    /// Plain 2D segment-intersection test (longitude/latitude treated as x/y) - matches how
    /// <c>WorldMapPanel</c> actually draws each leg as a straight line, so "do these two legs visibly
    /// cross" is answered the same way here as on the map. Adjacent legs sharing an endpoint are never
    /// tested against each other (the caller skips <c>j == i+1</c>), so this only needs to handle two
    /// genuinely separate segments.
    /// </summary>
    private static readonly double[] ShiftCandidatesDeg = [-360.0, 0.0, 360.0];

    private static bool SegmentsIntersect(Airport a1, Airport a2, Airport b1, Airport b2)
    {
        // A leg frequently crosses the antimeridian (e.g. Fiji at +177 to Samoa at -172) - a raw
        // straight-line test would treat that as one huge line spanning almost the entire map width (the
        // same distortion WorldMapPanel.DrawWrappedLine exists to avoid when actually rendering it),
        // which can then falsely "cross" something on the completely opposite side of the world. Each
        // segment's second point is unwrapped to be longitude-continuous with its first, then the second
        // segment is tried at a +/-360 degree shift as well as its raw position - covers every way two
        // segments that are really close together near the dateline could end up represented far apart.
        var a2Lon = a1.Longitude + GreatCircleMath.NormalizeLongitudeDelta(a2.Longitude - a1.Longitude);
        var b2Lon = b1.Longitude + GreatCircleMath.NormalizeLongitudeDelta(b2.Longitude - b1.Longitude);

        foreach (var shift in ShiftCandidatesDeg)
        {
            if (PlanarSegmentsIntersect(
                a1.Longitude, a1.Latitude, a2Lon, a2.Latitude,
                b1.Longitude + shift, b1.Latitude, b2Lon + shift, b2.Latitude))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PlanarSegmentsIntersect(double ax1, double ay1, double ax2, double ay2, double bx1, double by1, double bx2, double by2)
    {
        double Cross(double ox, double oy, double px, double py, double qx, double qy) =>
            (px - ox) * (qy - oy) - (py - oy) * (qx - ox);

        var d1 = Cross(bx1, by1, bx2, by2, ax1, ay1);
        var d2 = Cross(bx1, by1, bx2, by2, ax2, ay2);
        var d3 = Cross(ax1, ay1, ax2, ay2, bx1, by1);
        var d4 = Cross(ax1, ay1, ax2, ay2, bx2, by2);

        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

    private static bool IsBetter(
        (int Crossings, int SharpTurns, double TotalDistanceNm) candidate,
        (int Crossings, int SharpTurns, double TotalDistanceNm) baseline)
    {
        if (candidate.Crossings != baseline.Crossings)
        {
            return candidate.Crossings < baseline.Crossings;
        }

        if (candidate.SharpTurns != baseline.SharpTurns)
        {
            return candidate.SharpTurns < baseline.SharpTurns;
        }

        return candidate.TotalDistanceNm < baseline.TotalDistanceNm - 0.01;
    }

    private bool CoherenceTwoOptPass(List<Airport> path)
    {
        var improved = false;

        for (var i = 0; i < path.Count - 2; i++)
        {
            for (var j = i + 2; j < path.Count - 1; j++)
            {
                var baseline = EvaluatePath(path);
                path.Reverse(i + 1, j - i);
                var candidate = EvaluatePath(path);

                if (IsBetter(candidate, baseline))
                {
                    improved = true;
                }
                else
                {
                    path.Reverse(i + 1, j - i); // revert - not an improvement
                }
            }
        }

        return improved;
    }

    private bool CoherenceOrOptPass(List<Airport> path)
    {
        var improved = false;

        for (var k = 1; k < path.Count - 1; k++)
        {
            var node = path[k];
            var baseline = EvaluatePath(path);
            path.RemoveAt(k);

            var bestIndex = -1;
            var best = baseline;

            // i starts at 1, not 0: i=0 would insert the node BEFORE path[0], displacing the anchor out
            // of its fixed first position - confirmed as a real bug, not theoretical: it let the true
            // anchor (Tokyo) get pushed into the middle of its own fill chain and end up looking like an
            // ordinary revisited stop several hops later. The existing upper bound (i < path.Count, post-
            // removal) already can't push a node past the fixed end, since that would require i to reach
            // the pre-removal path.Count, one past the last valid index here.
            for (var i = 1; i < path.Count; i++)
            {
                if (i == k)
                {
                    continue; // reinserting at the same slot (post-removal) is a no-op
                }

                path.Insert(i, node);
                var candidate = EvaluatePath(path);
                path.RemoveAt(i);

                if (IsBetter(candidate, best))
                {
                    best = candidate;
                    bestIndex = i;
                }
            }

            path.Insert(bestIndex >= 0 ? bestIndex : k, node);
            improved |= bestIndex >= 0;
        }

        return improved;
    }
}
