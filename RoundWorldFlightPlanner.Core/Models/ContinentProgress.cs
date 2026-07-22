namespace RoundWorldFlightPlanner.Core.Models;

public class ContinentProgress
{
    public required string ContinentCode { get; set; }
    public required string ContinentName { get; set; }
    public int CountriesVisited { get; set; }
    public int CountriesTotal { get; set; }
    public double PercentComplete => CountriesTotal > 0 ? 100.0 * CountriesVisited / CountriesTotal : 0;

    /// <summary>Antarctica has no sovereign countries - it's landed/not-landed, not a percentage goal.</summary>
    public bool IsSpecialCase { get; set; }
    public bool GoalMet { get; set; }
}
