namespace RoundWorldFlightPlanner.Core.Models;

public class WeatherSnapshot
{
    public required string Icao { get; set; }
    public DateTime ObservationTimeUtc { get; set; }
    public required string RawMetar { get; set; }
    public int WindDirectionDeg { get; set; }
    public int WindSpeedKts { get; set; }
    public int WindGustKts { get; set; }
    public double VisibilitySm { get; set; }
    public double TemperatureC { get; set; }
    public double? DewpointC { get; set; }
    public double? AltimeterHpa { get; set; }
    public List<CloudLayer> CloudLayers { get; set; } = [];
}

/// <summary>One SKC/FEW/SCT/BKN/OVC layer from a METAR, e.g. "BKN020" -> Cover="BKN", BaseFt=2000.</summary>
public record CloudLayer(string Cover, int? BaseFt);
