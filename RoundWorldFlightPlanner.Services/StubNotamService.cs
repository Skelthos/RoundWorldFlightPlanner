using RoundWorldFlightPlanner.Core.Interfaces;

namespace RoundWorldFlightPlanner.Services;

/// <summary>
/// TODO: real NOTAM data needs an FAA API key registered at api.faa.gov - stubbed for now,
/// same treatment as PopulationLookupService.
/// </summary>
public class StubNotamService : INotamService
{
    public Task<string> GetNotamStatusAsync(string icao, CancellationToken cancellationToken = default) =>
        Task.FromResult($"NOTAM checking isn't wired up yet - check {icao} manually via your usual source before this flight.");
}
