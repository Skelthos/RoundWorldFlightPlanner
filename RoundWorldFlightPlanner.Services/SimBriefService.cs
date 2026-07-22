using System.Text.Json;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// Pulls your latest dispatched OFP from SimBrief's free API. Field layout confirmed against a
/// real dispatch (KVGT -&gt; KOAK). Fuel figures come back in whatever unit the pilot's SimBrief
/// profile uses (params.units: "kgs" or "lbs") - always converted to kg here so SimBriefFuelPlanKg
/// etc. are consistent regardless of the pilot's profile settings.
/// </summary>
public class SimBriefService(HttpClient httpClient) : ISimBriefService
{
    private const double LbToKg = 0.45359237;

    public async Task<SimBriefOfp?> FetchLatestOfpAsync(string simBriefUsername, CancellationToken cancellationToken = default)
    {
        // SimBrief returns HTTP 400 (not 200) for errors like "Unknown UserID", but still sends a
        // valid JSON body with the error status - don't EnsureSuccessStatusCode, just parse it.
        var url = $"https://www.simbrief.com/api/xml.fetcher.php?username={Uri.EscapeDataString(simBriefUsername)}&json=1";
        using var response = await httpClient.GetAsync(url, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("fetch", out var fetch)
            && fetch.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String
            && status.GetString()!.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!root.TryGetProperty("origin", out var origin) || !root.TryGetProperty("destination", out var destination))
        {
            return null;
        }

        var fuel = root.TryGetProperty("fuel", out var fuelElement) ? fuelElement : default;
        var general = root.TryGetProperty("general", out var generalElement) ? generalElement : default;
        var alternate = root.TryGetProperty("alternate", out var alternateElement) ? alternateElement : default;
        var parms = root.TryGetProperty("params", out var paramsElement) ? paramsElement : default;

        var unitsAreLbs = string.Equals(ReadString(parms, "units"), "lbs", StringComparison.OrdinalIgnoreCase);
        var toKg = unitsAreLbs ? LbToKg : 1.0;

        return new SimBriefOfp
        {
            OriginIcao = ReadString(origin, "icao_code") ?? string.Empty,
            DestinationIcao = ReadString(destination, "icao_code") ?? string.Empty,
            AlternateIcao = ReadString(alternate, "icao_code"),
            RouteText = ReadString(general, "route"),
            CruiseAltitudeFt = ReadInt(general, "initial_altitude"),
            PlanRampFuelKg = ReadDouble(fuel, "plan_ramp") * toKg,
            PlanTripFuelKg = ReadDouble(fuel, "enroute_burn") * toKg,
            PlanReserveFuelKg = ReadDouble(fuel, "reserve") * toKg,
            GeneratedUtc = ReadInt(parms, "time_generated") is var epoch and > 0
                ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime
                : DateTime.UtcNow,
        };
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
        && int.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), out var parsed)
            ? parsed
            : 0;

    private static double ReadDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
        && double.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), out var parsed)
            ? parsed
            : 0;
}
