using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.UI.Forms;

public class ProgressForm : Form
{
    public ProgressForm(IReadOnlyList<ContinentProgress> progress, int goalPercent)
    {
        Text = "Country Coverage Progress";
        Width = 520;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;

        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
        var rows = progress.Select(p => new
        {
            Continent = p.ContinentName,
            Visited = p.CountriesVisited,
            Total = p.CountriesTotal,
            Percent = p.IsSpecialCase ? "n/a" : $"{p.PercentComplete:F0}%",
            Goal = p.IsSpecialCase ? $"Land once" : $"{goalPercent}%",
            GoalMet = p.GoalMet ? "Yes" : "No",
        }).ToList();
        grid.DataSource = rows;

        Controls.Add(grid);
    }
}
