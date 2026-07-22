namespace RoundWorldFlightPlanner.Core.Interfaces;

public interface INotamService
{
    /// <summary>
    /// Returns a human-readable NOTAM status for the airport. TODO: real NOTAM data needs an FAA API
    /// key registered at api.faa.gov - stubbed for now, same treatment as IPopulationLookupService.
    /// </summary>
    Task<string> GetNotamStatusAsync(string icao, CancellationToken cancellationToken = default);
}
