using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Core.Interfaces;

public interface ISimBriefService
{
    /// <summary>Fetches the most recently dispatched OFP for the given SimBrief username/pilot ID. Null if none found.</summary>
    Task<SimBriefOfp?> FetchLatestOfpAsync(string simBriefUsername, CancellationToken cancellationToken = default);
}
