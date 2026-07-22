namespace RoundWorldFlightPlanner.UI;

internal static class AppPaths
{
    public static string DatabasePath
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RoundWorldFlightPlanner");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "flightplanner.db");
        }
    }
}
