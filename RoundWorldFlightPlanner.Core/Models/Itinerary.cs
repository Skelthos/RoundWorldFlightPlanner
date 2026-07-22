namespace RoundWorldFlightPlanner.Core.Models;

public class Itinerary
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedUtc { get; set; }

    public int AircraftId { get; set; }
    public Aircraft? Aircraft { get; set; }

    private int _happinessScore = 70;

    /// <summary>Wife happiness, 0-100. Below 25 forces a shopping layover.</summary>
    public int HappinessScore
    {
        get => _happinessScore;
        set => _happinessScore = Math.Clamp(value, 0, 100);
    }

    /// <summary>Target percentage of each continent's countries to land in (Antarctica excluded - one landing is enough there).</summary>
    public int CountryGoalPercent { get; set; } = 50;

    /// <summary>Target max block time per leg, in hours. Legs flagged IsOceanCrossing are exempt.</summary>
    public double MaxFlightTimeHours { get; set; } = 3;

    public List<FlightLeg> Legs { get; set; } = [];
    public List<HappinessEvent> HappinessEvents { get; set; } = [];
}
