using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Enums;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Final, global pass that removes legs which visibly cross each other on the map. Country-fill and
/// leg-splitting each make good LOCAL choices, but neither can see what the other (or a different
/// visit to the same region) did - so a stepping stone chosen for one leg can cut straight through a
/// leg planned elsewhere. This pass looks at the finished route as a whole. For every crossing pair it
/// tries a set of repairs - reversing the stretch between the two legs (2-opt), moving or dropping
/// blocks of up to three stops, swapping a stop for a better-placed airport, or inserting a new stop
/// to steer a leg around the other - and applies the best one that strictly reduces the crossing
/// count. If crossings remain that no single repair can fix, a seeded simulated-annealing walk (which
/// may accept a temporarily worse route) looks for two-step fixes, and a final greedy pass polishes.
/// Hop count and total distance are deliberately not constrained (a coherent, non-crossing flow beats
/// fewer or shorter legs); the only hard limit is the aircraft's realistic fuel range, and hops over
/// the softer max-flight-time cap are only ever a tie-break. Flown/in-progress legs and the named
/// spine waypoints never move, and no stop that is the only visit to a counted country is removed or
/// swapped for a different country, so country-coverage goals stay met.
/// </summary>
public class RouteUncrossingService(IRouteDistanceService distanceService)
{
    private const double RangeSafetyMargin = 0.85;
    private const double MinHopNm = 60;
    private const int PoolSize = 60;
    private const int MaxGreedyIterations = 300;
    private const int AnnealIterations = 5000;
    private const int MaxAnnealAttempts = 1;

    /// <summary>Stops the search on extreme settings (e.g. a 90% country goal) instead of freezing the UI for minutes; whatever is fixed by then is kept.</summary>
    private static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(12);

    private System.Diagnostics.Stopwatch _clock = new();

    private sealed record Node(Airport Airport, FlightLeg? Leg, bool Movable);

    private readonly record struct Score(int Crossings, double CapExcessNm, double RangeExcessNm, int SharpTurns, double DistanceNm);

    private readonly Dictionary<(int, int, int), List<Airport>> _poolCache = [];
    private double _strictCapNm;
    private double _maxRangeNm;

    /// <summary>Optional progress/diagnostic sink (used by test harnesses).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Returns the crossing count before and after, so callers/tests can report what happened.</summary>
    public (int Before, int After) RemoveCrossings(Itinerary itinerary, IReadOnlyList<Airport> allAirports)
    {
        var aircraft = itinerary.Aircraft;
        if (aircraft is null)
        {
            return (0, 0);
        }

        var maxTimeDistanceNm = aircraft.CruiseSpeedKts * itinerary.MaxFlightTimeHours;
        _maxRangeNm = aircraft.FuelBurnKgPerHour > 0
            ? aircraft.FuelCapacityKg / aircraft.FuelBurnKgPerHour * aircraft.CruiseSpeedKts * RangeSafetyMargin
            : maxTimeDistanceNm;
        _strictCapNm = Math.Min(maxTimeDistanceNm, _maxRangeNm);
        _poolCache.Clear();

        var orderedLegs = itinerary.Legs.OrderBy(l => l.SequenceNumber).ToList();
        if (orderedLegs.Count < 4)
        {
            return (0, 0);
        }

        var path = BuildPath(orderedLegs);
        var original = path.Select(n => n.Airport.Id).ToList();
        var originalEdges = Enumerable.Range(0, path.Count - 1).Select(k => (path[k].Airport.Id, path[k + 1].Airport.Id)).ToHashSet();
        var before = Evaluate(path).Crossings;
        if (before == 0)
        {
            return (0, 0);
        }

        _clock = System.Diagnostics.Stopwatch.StartNew();
        var clock = _clock;
        path = GreedyRepair(path, allAirports);
        Log?.Invoke($"greedy: {Evaluate(path).Crossings} crossings left after {clock.ElapsedMilliseconds}ms");
        // Different seeds explore different two-step fixes, so a stubborn leftover gets a few fresh tries
        // (each is seeded, so the overall result is still repeatable run to run).
        for (var attempt = 0; attempt < MaxAnnealAttempts && Evaluate(path).Crossings > 0 && _clock.Elapsed < TimeBudget; attempt++)
        {
            path = Anneal(path, allAirports, 20260923 + attempt);
            Log?.Invoke($"anneal {attempt + 1}: {Evaluate(path).Crossings} crossings left after {clock.ElapsedMilliseconds}ms");
            path = GreedyRepair(path, allAirports);
            Log?.Invoke($"polish {attempt + 1}: {Evaluate(path).Crossings} crossings left after {clock.ElapsedMilliseconds}ms");
        }

        path = ReduceNewCapExcess(path, allAirports, originalEdges);

        var after = Evaluate(path).Crossings;
        if (!path.Select(n => n.Airport.Id).SequenceEqual(original))
        {
            Rebuild(itinerary, orderedLegs, path);
        }

        return (before, after);
    }

    private static List<Node> BuildPath(List<FlightLeg> orderedLegs)
    {
        var path = new List<Node> { new(orderedLegs[0].DepartureAirport!, null, false) };
        for (var k = 0; k < orderedLegs.Count; k++)
        {
            var leg = orderedLegs[k];
            var next = k + 1 < orderedLegs.Count ? orderedLegs[k + 1] : null;
            var movable = !leg.IsSpineAnchor
                          && !leg.IsComplete
                          && leg.Phase == FlightPhase.NotStarted
                          && next is { IsComplete: false, Phase: FlightPhase.NotStarted };
            path.Add(new Node(leg.ArrivalAirport!, movable ? null : leg, movable));
        }

        return path;
    }

    private void Rebuild(Itinerary itinerary, List<FlightLeg> orderedLegs, List<Node> path)
    {
        var reused = path.Where(n => n.Leg is not null).Select(n => n.Leg!).ToHashSet();
        foreach (var old in orderedLegs.Where(l => !reused.Contains(l)))
        {
            itinerary.Legs.Remove(old);
        }

        var rebuilt = new List<FlightLeg>();
        for (var k = 1; k < path.Count; k++)
        {
            var from = path[k - 1].Airport;
            var to = path[k].Airport;
            var distanceNm = distanceService.GreatCircleDistanceNm(from, to);
            var leg = path[k].Leg;
            if (leg is null)
            {
                leg = new FlightLeg
                {
                    ItineraryId = itinerary.Id,
                    ArrivalAirportId = to.Id,
                    ArrivalAirport = to,
                    PlannedCruiseSpeedKts = itinerary.Aircraft?.CruiseSpeedKts ?? 0,
                };
                itinerary.Legs.Add(leg);
            }

            if (leg.DepartureAirportId != from.Id)
            {
                leg.DepartureAirportId = from.Id;
                leg.DepartureAirport = from;
            }

            leg.PlannedDistanceNm = distanceNm;
            leg.IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(from, to, distanceNm);
            rebuilt.Add(leg);
        }

        for (var i = 0; i < rebuilt.Count; i++)
        {
            rebuilt[i].SequenceNumber = i + 1;
        }
    }

    // ------------------------------------------------------------ greedy repair

    private List<Node> GreedyRepair(List<Node> path, IReadOnlyList<Airport> allAirports)
    {
        var unfixable = new HashSet<(int, int, int, int)>();
        for (var iteration = 0; iteration < MaxGreedyIterations; iteration++)
        {
            if (_clock.Elapsed > TimeBudget)
            {
                break;
            }

            var current = Evaluate(path);
            if (current.Crossings == 0)
            {
                break;
            }

            var crossing = FindCrossings(path).FirstOrDefault(c => !unfixable.Contains(KeyFor(path, c)));
            if (crossing == default)
            {
                break;
            }

            var best = FindBestRepair(path, crossing, current, allAirports);
            if (best is null)
            {
                unfixable.Add(KeyFor(path, crossing));
                continue;
            }

            path = best;
        }

        return path;
    }

    private static (int, int, int, int) KeyFor(List<Node> path, (int I, int J) c) =>
        (path[c.I].Airport.Id, path[c.I + 1].Airport.Id, path[c.J].Airport.Id, path[c.J + 1].Airport.Id);

    private List<Node>? FindBestRepair(List<Node> path, (int I, int J) crossing, Score baseline, IReadOnlyList<Airport> allAirports)
    {
        List<Node>? best = null;
        var bestScore = baseline;
        var inPath = path.Select(n => n.Airport.Id).ToHashSet();
        var last = path.Count - 1;

        void Consider(List<Node> candidate)
        {
            // Counting crossings is far cheaper than the full score (no trig), and almost every candidate
            // fails this test - so only the survivors pay for distances and bearings.
            if (CountCrossings(candidate) >= baseline.Crossings)
            {
                return;
            }

            var score = Evaluate(candidate);
            if (score.Crossings >= baseline.Crossings || score.RangeExcessNm > baseline.RangeExcessNm + 1e-6)
            {
                return;
            }

            if (best is null || IsBetter(score, bestScore))
            {
                best = candidate;
                bestScore = score;
            }
        }

        bool InRange(Airport a, Airport b) => distanceService.GreatCircleDistanceNm(a, b) <= _maxRangeNm;
        bool AllMovable(int from, int to) => from >= 1 && to <= last - 1 && Enumerable.Range(from, to - from + 1).All(k => path[k].Movable);

        var (i, j) = crossing;

        // 2-opt reversals: the classic one between the two crossing legs, plus nearby variants (the
        // stretch that needs flipping often starts or ends a stop or two away from the exact crossing).
        for (var a = i - 3; a <= i + 1; a++)
        {
            for (var b = j - 1; b <= j + 3; b++)
            {
                if (a < 0 || b <= a + 1 || b >= last || !AllMovable(a + 1, b))
                {
                    continue;
                }

                var reversed = new List<Node>(path);
                reversed.Reverse(a + 1, b - a);
                Consider(reversed);
            }
        }

        var hot = Enumerable.Range(i - 3, 7).Concat(Enumerable.Range(j - 2, 7))
            .Distinct().Where(k => k >= 1 && k <= last - 1 && path[k].Movable).ToList();

        // Block moves: relocate (or drop) 1-3 consecutive movable stops, forward or reversed.
        foreach (var start in hot)
        {
            for (var length = 1; length <= 3; length++)
            {
                var end = start + length - 1;
                if (!AllMovable(start, end))
                {
                    break;
                }

                var block = path.GetRange(start, length);
                var rest = new List<Node>(path);
                rest.RemoveRange(start, length);

                if (Enumerable.Range(start, length).All(k => !IsNeeded(path, k)))
                {
                    Consider(rest);
                }

                for (var p = 0; p < rest.Count - 1; p++)
                {
                    if (p == start - 1)
                    {
                        continue; // same slot
                    }

                    foreach (var orientation in new[] { block, Enumerable.Reverse(block).ToList() })
                    {
                        if (!InRange(rest[p].Airport, orientation[0].Airport) || !InRange(orientation[^1].Airport, rest[p + 1].Airport))
                        {
                            continue;
                        }

                        var moved = new List<Node>(rest);
                        moved.InsertRange(p + 1, orientation);
                        Consider(moved);
                    }
                }
            }
        }

        // Replace a single stop with a better-placed airport (same country if it is the only visit to it).
        foreach (var m in hot)
        {
            var needed = IsNeeded(path, m);
            foreach (var candidate in PoolFor(allAirports, inPath, path[m - 1].Airport, path[m + 1].Airport, needed ? path[m].Airport : null))
            {
                var replaced = new List<Node>(path) { [m] = new Node(candidate, null, true) };
                Consider(replaced);
            }
        }

        // Insert a brand-new stop into a leg near the crossing to steer it around the other one.
        foreach (var edge in new[] { i - 1, i, i + 1, j - 1, j, j + 1 }.Distinct().Where(e => e >= 0 && e < last))
        {
            foreach (var candidate in PoolFor(allAirports, inPath, path[edge].Airport, path[edge + 1].Airport, null))
            {
                var inserted = new List<Node>(path);
                inserted.Insert(edge + 1, new Node(candidate, null, true));
                Consider(inserted);
            }
        }

        return best;
    }

    /// <summary>
    /// Uncrossing can leave a hop longer than the max-flight-time cap (it only has to stay within fuel
    /// range). For every such hop this pass itself created, tries adding a stop into it - accepted only
    /// if it shortens the overrun without adding a crossing - so the fix for a crossing doesn't quietly
    /// trade it for an overlong leg.
    /// </summary>
    private List<Node> ReduceNewCapExcess(List<Node> path, IReadOnlyList<Airport> allAirports, HashSet<(int, int)> originalEdges)
    {
        for (var pass = 0; pass < 200; pass++)
        {
            var baseline = Evaluate(path);
            var inPath = path.Select(n => n.Airport.Id).ToHashSet();
            List<Node>? best = null;
            var bestScore = baseline;

            for (var e = 0; e < path.Count - 1; e++)
            {
                if (originalEdges.Contains((path[e].Airport.Id, path[e + 1].Airport.Id))
                    || distanceService.GreatCircleDistanceNm(path[e].Airport, path[e + 1].Airport) <= _strictCapNm)
                {
                    continue;
                }

                foreach (var candidate in PoolFor(allAirports, inPath, path[e].Airport, path[e + 1].Airport, null))
                {
                    var inserted = new List<Node>(path);
                    inserted.Insert(e + 1, new Node(candidate, null, true));
                    if (CountCrossings(inserted) > baseline.Crossings)
                    {
                        continue;
                    }

                    var score = Evaluate(inserted);
                    if (score.CapExcessNm < bestScore.CapExcessNm - 1e-6)
                    {
                        best = inserted;
                        bestScore = score;
                    }
                }
            }

            if (best is null)
            {
                break;
            }

            path = best;
        }

        return path;
    }

    // ------------------------------------------------------------ annealing

    /// <summary>
    /// Fallback for crossings no single repair can fix: a seeded (so repeatable) simulated-annealing walk
    /// that may accept a temporarily worse route, which is what two-step fixes need - e.g. moving a stop
    /// out of one leg's way only pays off once a second stop has moved too. Returns the best route seen
    /// (never worse than <paramref name="start"/>).
    /// </summary>
    private List<Node> Anneal(List<Node> start, IReadOnlyList<Airport> allAirports, int seed)
    {
        var rng = new Random(seed);
        var startScore = Evaluate(start);
        var current = start;
        var currentScore = startScore;
        var best = start;
        var bestScore = startScore;

        for (var step = 0; step < AnnealIterations && bestScore.Crossings > 0; step++)
        {
            if ((step & 63) == 0 && _clock.Elapsed > TimeBudget)
            {
                break;
            }

            var temperature = 6.0 * Math.Pow(0.3 / 6.0, (double)step / AnnealIterations);
            var crossings = FindCrossings(current);
            if (crossings.Count == 0)
            {
                best = current;
                break;
            }

            var candidate = RandomMove(current, crossings[rng.Next(crossings.Count)], allAirports, rng);
            if (candidate is null)
            {
                continue;
            }

            // A move that adds two or more crossings is never worth the full (trig-heavy) score.
            if (CountCrossings(candidate) > currentScore.Crossings + 1)
            {
                continue;
            }

            var score = Evaluate(candidate);
            if (score.RangeExcessNm > startScore.RangeExcessNm + 1e-6)
            {
                continue;
            }

            var delta = Energy(score) - Energy(currentScore);
            if (delta <= 0 || rng.NextDouble() < Math.Exp(-delta / temperature))
            {
                current = candidate;
                currentScore = score;
                if (IsBetter(score, bestScore))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    private static double Energy(Score s) => s.Crossings * 10 + s.CapExcessNm / 200 + s.SharpTurns * 0.5 + s.DistanceNm / 20000;

    private List<Node>? RandomMove(List<Node> path, (int I, int J) crossing, IReadOnlyList<Airport> allAirports, Random rng)
    {
        var last = path.Count - 1;
        var (i, j) = crossing;
        var center = rng.Next(2) == 0 ? i : j;
        var hot = Enumerable.Range(center - 2, 6).Where(k => k >= 1 && k <= last - 1 && path[k].Movable).ToList();
        var inPath = path.Select(n => n.Airport.Id).ToHashSet();

        List<Airport> Pool(Airport from, Airport to, Airport? sameCountryAs) => PoolFor(allAirports, inPath, from, to, sameCountryAs);

        switch (rng.Next(5))
        {
            case 0 when hot.Count > 0:
            {
                var m = hot[rng.Next(hot.Count)];
                var pool = Pool(path[m - 1].Airport, path[m + 1].Airport, IsNeeded(path, m) ? path[m].Airport : null);
                if (pool.Count == 0)
                {
                    return null;
                }

                return new List<Node>(path) { [m] = new Node(pool[rng.Next(pool.Count)], null, true) };
            }

            case 1 when hot.Count > 0:
            {
                var start = hot[rng.Next(hot.Count)];
                var end = Math.Min(start + rng.Next(3), last - 1);
                if (Enumerable.Range(start, end - start + 1).Any(k => !path[k].Movable))
                {
                    return null;
                }

                var block = path.GetRange(start, end - start + 1);
                var rest = new List<Node>(path);
                rest.RemoveRange(start, block.Count);
                var p = rng.Next(rest.Count - 1);
                if (p == start - 1)
                {
                    return null;
                }

                if (rng.Next(2) == 0)
                {
                    block.Reverse();
                }

                if (distanceService.GreatCircleDistanceNm(rest[p].Airport, block[0].Airport) > _maxRangeNm
                    || distanceService.GreatCircleDistanceNm(block[^1].Airport, rest[p + 1].Airport) > _maxRangeNm)
                {
                    return null;
                }

                rest.InsertRange(p + 1, block);
                return rest;
            }

            case 2:
            {
                var a = i - 3 + rng.Next(5);
                var b = j - 1 + rng.Next(5);
                if (a < 0 || b <= a + 1 || b >= last || Enumerable.Range(a + 1, b - a).Any(k => !path[k].Movable))
                {
                    return null;
                }

                var reversed = new List<Node>(path);
                reversed.Reverse(a + 1, b - a);
                return reversed;
            }

            case 3:
            {
                var edges = new[] { i - 1, i, i + 1, j - 1, j, j + 1 }.Where(e => e >= 0 && e < last).ToList();
                var edge = edges[rng.Next(edges.Count)];
                var pool = Pool(path[edge].Airport, path[edge + 1].Airport, null);
                if (pool.Count == 0)
                {
                    return null;
                }

                var inserted = new List<Node>(path);
                inserted.Insert(edge + 1, new Node(pool[rng.Next(pool.Count)], null, true));
                return inserted;
            }

            case 4 when hot.Count > 0:
            {
                var m = hot[rng.Next(hot.Count)];
                if (IsNeeded(path, m))
                {
                    return null;
                }

                var without = new List<Node>(path);
                without.RemoveAt(m);
                return without;
            }

            default:
                return null;
        }
    }

    // ------------------------------------------------------------ candidates

    /// <summary>A stop is "needed" if it is the only visit to a country that counts toward its continent's goal.</summary>
    private static bool IsNeeded(List<Node> path, int index)
    {
        var airport = path[index].Airport;
        if (!ContinentCountryReference.CountriesByContinent.TryGetValue(airport.ContinentCode, out var valid)
            || !valid.Contains(airport.Country))
        {
            return false;
        }

        return path.Count(n => n.Airport.ContinentCode == airport.ContinentCode && n.Airport.Country == airport.Country) == 1;
    }

    private List<Airport> PoolFor(IReadOnlyList<Airport> allAirports, HashSet<int> inPath, Airport from, Airport to, Airport? sameCountryAs)
    {
        var key = (from.Id, to.Id, sameCountryAs?.Id ?? -1);
        if (!_poolCache.TryGetValue(key, out var pool))
        {
            pool = CheapestInsertions(allAirports, [], from, to, sameCountryAs);
            _poolCache[key] = pool;
        }

        return pool.Where(a => !inPath.Contains(a.Id)).ToList();
    }

    /// <summary>
    /// Airports that could sit between <paramref name="from"/> and <paramref name="to"/> with the smallest
    /// added distance, both hops within range and long enough to be a real stop. When
    /// <paramref name="sameCountryAs"/> is given (the stop being replaced is the only visit to its country)
    /// only that country's airports qualify.
    /// </summary>
    private List<Airport> CheapestInsertions(IReadOnlyList<Airport> allAirports, HashSet<int> inPath, Airport from, Airport to, Airport? sameCountryAs)
    {
        var pool = new List<(Airport Airport, double DetourNm)>();
        var directNm = distanceService.GreatCircleDistanceNm(from, to);
        var latWindowDeg = _maxRangeNm / 60.0 + 1;
        foreach (var candidate in allAirports)
        {
            if (inPath.Contains(candidate.Id))
            {
                continue;
            }

            if (sameCountryAs is not null
                && (candidate.ContinentCode != sameCountryAs.ContinentCode || candidate.Country != sameCountryAs.Country))
            {
                continue;
            }

            if (Math.Abs(candidate.Latitude - from.Latitude) > latWindowDeg || Math.Abs(candidate.Latitude - to.Latitude) > latWindowDeg)
            {
                continue;
            }

            var toCandidateNm = distanceService.GreatCircleDistanceNm(from, candidate);
            if (toCandidateNm < MinHopNm || toCandidateNm > _maxRangeNm)
            {
                continue;
            }

            var fromCandidateNm = distanceService.GreatCircleDistanceNm(candidate, to);
            if (fromCandidateNm < MinHopNm || fromCandidateNm > _maxRangeNm)
            {
                continue;
            }

            pool.Add((candidate, toCandidateNm + fromCandidateNm - directNm));
        }

        return pool.OrderBy(p => p.DetourNm).Take(PoolSize).Select(p => p.Airport).ToList();
    }

    // ------------------------------------------------------------ evaluation

    private static bool IsBetter(Score candidate, Score baseline)
    {
        if (candidate.Crossings != baseline.Crossings)
        {
            return candidate.Crossings < baseline.Crossings;
        }

        if (Math.Abs(candidate.CapExcessNm - baseline.CapExcessNm) > 1e-6)
        {
            return candidate.CapExcessNm < baseline.CapExcessNm;
        }

        if (candidate.SharpTurns != baseline.SharpTurns)
        {
            return candidate.SharpTurns < baseline.SharpTurns;
        }

        return candidate.DistanceNm < baseline.DistanceNm - 0.01;
    }

    private Score Evaluate(List<Node> path)
    {
        var edges = BuildEdges(path);
        double distanceNm = 0;
        double excessNm = 0;
        double rangeExcessNm = 0;
        var sharpTurns = 0;
        double? previousBearing = null;
        for (var e = 0; e < edges.Length; e++)
        {
            var from = path[e].Airport;
            var to = path[e + 1].Airport;
            var d = distanceService.GreatCircleDistanceNm(from, to);
            distanceNm += d;
            excessNm += Math.Max(0, d - _strictCapNm);
            rangeExcessNm += Math.Max(0, d - _maxRangeNm);

            var bearing = GreatCircleMath.InitialBearingDeg(from, to);
            if (previousBearing.HasValue && Math.Abs(GreatCircleMath.NormalizeLongitudeDelta(bearing - previousBearing.Value)) > 120)
            {
                sharpTurns++;
            }

            previousBearing = bearing;
        }

        return new Score(CountCrossings(edges), excessNm, rangeExcessNm, sharpTurns, distanceNm);
    }

    private static int CountCrossings(List<Node> path) => CountCrossings(BuildEdges(path));

    private static int CountCrossings(Edge[] edges)
    {
        var crossings = 0;
        for (var a = 0; a < edges.Length; a++)
        {
            for (var b = a + 2; b < edges.Length; b++)
            {
                if (Crosses(edges[a], edges[b]))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static List<(int I, int J)> FindCrossings(List<Node> path)
    {
        var edges = BuildEdges(path);
        var result = new List<(int, int)>();
        for (var a = 0; a < edges.Length; a++)
        {
            for (var b = a + 2; b < edges.Length; b++)
            {
                if (Crosses(edges[a], edges[b]))
                {
                    result.Add((a, b));
                }
            }
        }

        return result;
    }

    private readonly record struct Edge(int FromId, int ToId, double X1, double Y1, double X2, double Y2, double MinX, double MaxX, double MinY, double MaxY);

    private static Edge[] BuildEdges(List<Node> path)
    {
        var edges = new Edge[path.Count - 1];
        for (var e = 0; e < edges.Length; e++)
        {
            var from = path[e].Airport;
            var to = path[e + 1].Airport;

            // Unwrap so an edge crossing the antimeridian is one continuous line, exactly as the map draws it.
            var x2 = from.Longitude + GreatCircleMath.NormalizeLongitudeDelta(to.Longitude - from.Longitude);
            edges[e] = new Edge(
                from.Id, to.Id, from.Longitude, from.Latitude, x2, to.Latitude,
                Math.Min(from.Longitude, x2), Math.Max(from.Longitude, x2),
                Math.Min(from.Latitude, to.Latitude), Math.Max(from.Latitude, to.Latitude));
        }

        return edges;
    }

    private static readonly double[] Shifts = [0.0, -360.0, 360.0];

    private static bool Crosses(Edge a, Edge b)
    {
        // Two legs meeting at a shared airport touch there - that is not a crossing.
        if (a.FromId == b.FromId || a.FromId == b.ToId || a.ToId == b.FromId || a.ToId == b.ToId)
        {
            return false;
        }

        if (a.MaxY < b.MinY || b.MaxY < a.MinY)
        {
            return false;
        }

        foreach (var shift in Shifts)
        {
            if (b.MaxX + shift < a.MinX || b.MinX + shift > a.MaxX)
            {
                continue;
            }

            if (PlanarSegmentsIntersect(a.X1, a.Y1, a.X2, a.Y2, b.X1 + shift, b.Y1, b.X2 + shift, b.Y2))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PlanarSegmentsIntersect(double ax1, double ay1, double ax2, double ay2, double bx1, double by1, double bx2, double by2)
    {
        static double Cross(double ox, double oy, double px, double py, double qx, double qy) =>
            (px - ox) * (qy - oy) - (py - oy) * (qx - ox);

        var d1 = Cross(bx1, by1, bx2, by2, ax1, ay1);
        var d2 = Cross(bx1, by1, bx2, by2, ax2, ay2);
        var d3 = Cross(ax1, ay1, ax2, ay2, bx1, by1);
        var d4 = Cross(ax1, ay1, ax2, ay2, bx2, by2);

        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }
}
