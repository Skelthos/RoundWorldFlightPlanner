using System.Globalization;
using System.Text.Json;
using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Services;

/// <summary>Live METAR pull from the free, keyless aviationweather.gov Data API.</summary>
public class WeatherService(HttpClient httpClient) : IWeatherService
{
    public async Task<WeatherSnapshot?> GetCurrentWeatherAsync(string icao, CancellationToken cancellationToken = default)
    {
        var url = $"https://aviationweather.gov/api/data/metar?ids={Uri.EscapeDataString(icao)}&format=json";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var report = document.RootElement.EnumerateArray().FirstOrDefault();
        if (report.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new WeatherSnapshot
        {
            Icao = icao,
            ObservationTimeUtc = report.TryGetProperty("obsTime", out var obsTime)
                ? DateTimeOffset.FromUnixTimeSeconds(obsTime.GetInt64()).UtcDateTime
                : DateTime.UtcNow,
            RawMetar = report.TryGetProperty("rawOb", out var rawOb) ? rawOb.GetString() ?? string.Empty : string.Empty,
            WindDirectionDeg = ReadWindDirection(report),
            WindSpeedKts = report.TryGetProperty("wspd", out var wspd) && wspd.ValueKind == JsonValueKind.Number
                ? wspd.GetInt32()
                : 0,
            WindGustKts = report.TryGetProperty("wgst", out var wgst) && wgst.ValueKind == JsonValueKind.Number
                ? wgst.GetInt32()
                : 0,
            VisibilitySm = ReadVisibility(report),
            TemperatureC = report.TryGetProperty("temp", out var temp) && temp.ValueKind == JsonValueKind.Number
                ? temp.GetDouble()
                : 0,
            DewpointC = report.TryGetProperty("dewp", out var dewp) && dewp.ValueKind == JsonValueKind.Number
                ? dewp.GetDouble()
                : null,
            AltimeterHpa = report.TryGetProperty("altim", out var altim) && altim.ValueKind == JsonValueKind.Number
                ? altim.GetDouble()
                : null,
            CloudLayers = ReadCloudLayers(report),
        };
    }

    public async Task<WeatherForecast?> GetForecastAsync(string icao, CancellationToken cancellationToken = default)
    {
        var url = $"https://aviationweather.gov/api/data/taf?ids={Uri.EscapeDataString(icao)}&format=json";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var report = document.RootElement.EnumerateArray().FirstOrDefault();
        if (report.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new WeatherForecast
        {
            Icao = icao,
            IssueTimeUtc = report.TryGetProperty("issueTime", out var issueTime) && issueTime.ValueKind == JsonValueKind.String
                && DateTime.TryParse(issueTime.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedIssueTime)
                ? parsedIssueTime
                : DateTime.UtcNow,
            RawTaf = report.TryGetProperty("rawTAF", out var rawTaf) ? rawTaf.GetString() ?? string.Empty : string.Empty,
        };
    }

    public async Task<IReadOnlyList<string>> GetEnrouteHazardsAsync(CancellationToken cancellationToken = default)
    {
        // US/NWS-scoped coverage only - international legs won't have AIRMET/SIGMET data from this source.
        const string url = "https://aviationweather.gov/api/data/airsigmet?format=json";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.EnumerateArray()
            .Where(e => e.TryGetProperty("rawAirSigmet", out _))
            .Select(e => e.GetProperty("rawAirSigmet").GetString() ?? string.Empty)
            .Where(text => text.Length > 0)
            .ToList();
    }

    private static int ReadWindDirection(JsonElement report)
    {
        // wdir is usually a number but can be "VRB" for variable wind.
        if (!report.TryGetProperty("wdir", out var wdir))
        {
            return 0;
        }

        return wdir.ValueKind switch
        {
            JsonValueKind.Number => wdir.GetInt32(),
            _ => 0,
        };
    }

    private static List<CloudLayer> ReadCloudLayers(JsonElement report)
    {
        if (!report.TryGetProperty("clouds", out var clouds) || clouds.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var layers = new List<CloudLayer>();
        foreach (var layer in clouds.EnumerateArray())
        {
            if (!layer.TryGetProperty("cover", out var cover) || cover.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            int? baseFt = layer.TryGetProperty("base", out var baseElement) && baseElement.ValueKind == JsonValueKind.Number
                ? baseElement.GetInt32()
                : null;
            layers.Add(new CloudLayer(cover.GetString() ?? string.Empty, baseFt));
        }

        return layers;
    }

    private static double ReadVisibility(JsonElement report)
    {
        // visib can be a plain number or a string like "10+".
        if (!report.TryGetProperty("visib", out var visib))
        {
            return 0;
        }

        return visib.ValueKind switch
        {
            JsonValueKind.Number => visib.GetDouble(),
            JsonValueKind.String => double.TryParse(
                visib.GetString()?.TrimEnd('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0,
            _ => 0,
        };
    }
}
