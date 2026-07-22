namespace RoundWorldFlightPlanner.Core.Models;

/// <summary>An item on the persistent cargo manifest, e.g. one of the wife's outfits picked up on a shopping stop.</summary>
public class CargoItem
{
    public int Id { get; set; }
    public required string Description { get; set; }
    public double WeightKg { get; set; }
    public bool IsWifeOutfit { get; set; }

    /// <summary>True once the pilot ships this item home to fix weight &amp; balance - excluded from W&amp;B totals, but stays in the log.</summary>
    public bool IsShippedHome { get; set; }

    public int AcquiredOnLegId { get; set; }
    public FlightLeg? AcquiredOnLeg { get; set; }
}
