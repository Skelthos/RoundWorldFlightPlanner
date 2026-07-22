using RoundWorldFlightPlanner.Core.Interfaces;
using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Services;

namespace RoundWorldFlightPlanner.UI.Forms;

/// <summary>
/// Picks the next leg's arrival airport. Departure is locked to the continuity rule. Candidates are
/// ranked by not-yet-visited country in the current continent first, then by distance - this is
/// the assist that gets you toward your country-coverage goal without a hand-authored stop list.
/// </summary>
public class AddLegForm : Form
{
    private readonly record struct Candidate(Airport Airport, double DistanceNm, bool IsNewCountryInContinent);

    private readonly Airport _departure;
    private readonly List<Candidate> _candidates;
    private readonly TextBox _searchBox;
    private readonly ListBox _resultsList;
    private readonly CheckBox _oceanCrossingCheck;

    public Airport? SelectedDeparture { get; }
    public Airport? SelectedArrival { get; private set; }
    public bool IsOceanCrossing => _oceanCrossingCheck.Checked;

    public AddLegForm(IReadOnlyList<Airport> airports, Airport departure, Itinerary itinerary, IRouteDistanceService distanceService)
    {
        SelectedDeparture = departure;
        _departure = departure;

        var visitedCountriesInContinent = itinerary.Legs
            .Where(l => l.IsComplete && l.ArrivalAirport is not null && l.ArrivalAirport.ContinentCode == departure.ContinentCode)
            .Select(l => l.ArrivalAirport!.Country)
            .ToHashSet();

        _candidates = airports
            .Where(a => a.Icao != departure.Icao)
            .Select(a => new Candidate(
                a,
                distanceService.GreatCircleDistanceNm(departure, a),
                a.ContinentCode == departure.ContinentCode && !visitedCountriesInContinent.Contains(a.Country)))
            .OrderByDescending(c => c.IsNewCountryInContinent)
            .ThenBy(c => c.DistanceNm)
            .ToList();

        Text = "Add Next Leg";
        Width = 620;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var departureLabel = new Label
        {
            Text = $"Departing from: {departure.Icao} - {departure.Name}\r\n(locked - must depart from where the last leg landed)",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 12, 12, 0),
        };

        var hintLabel = new Label
        {
            Text = $"Ranked: not-yet-visited countries in {departure.ContinentCode} first, then nearest. [NEW] = new country toward your goal.",
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = Color.DimGray,
        };

        _searchBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(12), PlaceholderText = "Filter by ICAO, city, or country..." };
        _searchBox.TextChanged += (_, _) => PopulateResults();

        _resultsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _resultsList.SelectedIndexChanged += (_, _) => UpdateOceanCrossingSuggestion();

        _oceanCrossingCheck = new CheckBox
        {
            Text = "Ocean/desert crossing (exempt from max flight time) - auto-suggested, override if wrong",
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(12, 0, 0, 0),
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        okButton.Click += (_, _) => ResolveSelection();
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(_resultsList);
        Controls.Add(_oceanCrossingCheck);
        Controls.Add(_searchBox);
        Controls.Add(hintLabel);
        Controls.Add(departureLabel);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        PopulateResults();
    }

    private void PopulateResults()
    {
        var filter = _searchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _candidates
            : _candidates.Where(c =>
                c.Airport.Icao.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                c.Airport.City.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                c.Airport.Country.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                c.Airport.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _resultsList.BeginUpdate();
        _resultsList.Items.Clear();
        foreach (var candidate in filtered.Take(100))
        {
            var flag = candidate.IsNewCountryInContinent ? " [NEW]" : "";
            _resultsList.Items.Add(new ListEntry(candidate.Airport,
                $"{candidate.Airport.Icao} - {candidate.Airport.Name} ({candidate.Airport.City}, {candidate.Airport.Country}) - {candidate.DistanceNm:F0} nm{flag}"));
        }
        _resultsList.EndUpdate();
    }

    private void UpdateOceanCrossingSuggestion()
    {
        if (_resultsList.SelectedItem is ListEntry entry)
        {
            var distanceNm = _candidates.First(c => c.Airport.Icao == entry.Airport.Icao).DistanceNm;
            _oceanCrossingCheck.Checked = ItineraryContinuityValidator.SuggestIsOceanCrossing(_departure, entry.Airport, distanceNm);
        }
    }

    private void ResolveSelection()
    {
        if (_resultsList.SelectedItem is ListEntry entry)
        {
            SelectedArrival = entry.Airport;
            return;
        }

        MessageBox.Show("Pick an airport from the list.");
        DialogResult = DialogResult.None;
    }

    private sealed class ListEntry(Airport airport, string display)
    {
        public Airport Airport { get; } = airport;
        public override string ToString() => display;
    }
}
