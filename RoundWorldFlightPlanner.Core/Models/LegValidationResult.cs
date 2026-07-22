namespace RoundWorldFlightPlanner.Core.Models;

public class LegValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    public void AddError(string message)
    {
        Errors.Add(message);
        IsValid = false;
    }

    public void AddWarning(string message) => Warnings.Add(message);
}
