using Microsoft.EntityFrameworkCore;
using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;

namespace RoundWorldFlightPlanner.UI.Forms;

/// <summary>Lists all saved trips: continue one, delete one, or start a new round-the-world flight.</summary>
public class TripManagerForm : Form
{
    public enum ManagerResult { Continue, StartNew, Cancelled }

    private readonly FlightPlannerDbContext _context;
    private readonly ListBox _tripList;
    private readonly Button _continueButton;
    private readonly Button _deleteButton;
    private List<Itinerary> _itineraries = [];

    public ManagerResult Result { get; private set; } = ManagerResult.Cancelled;
    public Itinerary? SelectedItinerary { get; private set; }

    public TripManagerForm(FlightPlannerDbContext context, bool allowCancel)
    {
        _context = context;

        Text = "Manage Trips";
        Width = 620;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        if (!allowCancel)
        {
            ControlBox = false;
        }

        _tripList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _tripList.DoubleClick += (_, _) => OnContinueClicked();

        _continueButton = new Button { Text = "Continue Selected", Dock = DockStyle.Top, Height = 36 };
        _continueButton.Click += (_, _) => OnContinueClicked();

        _deleteButton = new Button { Text = "Delete Selected", Dock = DockStyle.Top, Height = 36, ForeColor = Color.DarkRed };
        _deleteButton.Click += (_, _) => OnDeleteClicked();

        var newTripButton = new Button { Text = "Start New World Flight", Dock = DockStyle.Bottom, Height = 36 };
        newTripButton.Click += (_, _) => { Result = ManagerResult.StartNew; Close(); };

        Controls.Add(_tripList);
        Controls.Add(_deleteButton);
        Controls.Add(_continueButton);
        Controls.Add(newTripButton);

        if (allowCancel)
        {
            var cancelButton = new Button { Text = "Cancel", Dock = DockStyle.Bottom, Height = 36 };
            cancelButton.Click += (_, _) => { Result = ManagerResult.Cancelled; Close(); };
            Controls.Add(cancelButton);
        }

        LoadTrips();
    }

    private void LoadTrips()
    {
        _itineraries = _context.Itineraries
            .Include(i => i.Aircraft)
            .Include(i => i.Legs)
            .OrderByDescending(i => i.CreatedUtc)
            .ToList();

        _tripList.Items.Clear();
        foreach (var itinerary in _itineraries)
        {
            var completed = itinerary.Legs.Count(l => l.IsComplete);
            _tripList.Items.Add(
                $"{itinerary.Name} - {itinerary.Aircraft?.Name} - leg {completed}/{itinerary.Legs.Count} complete - " +
                $"happiness {itinerary.HappinessScore}/100 - created {itinerary.CreatedUtc:d}");
        }

        if (_tripList.Items.Count > 0)
        {
            _tripList.SelectedIndex = 0;
        }

        var hasTrips = _itineraries.Count > 0;
        _continueButton.Enabled = hasTrips;
        _deleteButton.Enabled = hasTrips;
    }

    private bool TryGetSelected(out Itinerary itinerary)
    {
        itinerary = null!;
        if (_tripList.SelectedIndex < 0 || _tripList.SelectedIndex >= _itineraries.Count)
        {
            MessageBox.Show("Pick a trip first.");
            return false;
        }

        itinerary = _itineraries[_tripList.SelectedIndex];
        return true;
    }

    private void OnContinueClicked()
    {
        if (!TryGetSelected(out var itinerary))
        {
            return;
        }

        SelectedItinerary = itinerary;
        Result = ManagerResult.Continue;
        Close();
    }

    private void OnDeleteClicked()
    {
        if (!TryGetSelected(out var itinerary))
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Permanently delete \"{itinerary.Name}\"? This cannot be undone.",
            "Delete Trip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        var fullItinerary = _context.Itineraries
            .Include(i => i.Legs)
            .Include(i => i.HappinessEvents)
            .First(i => i.Id == itinerary.Id);

        // Belt-and-suspenders: explicitly remove CargoItems too, rather than relying solely on the
        // database-level cascade, in case foreign key enforcement isn't active for this connection.
        var legIds = fullItinerary.Legs.Select(l => l.Id).ToList();
        var cargoItems = _context.CargoItems.Where(c => legIds.Contains(c.AcquiredOnLegId)).ToList();
        _context.CargoItems.RemoveRange(cargoItems);

        _context.Itineraries.Remove(fullItinerary);
        _context.SaveChanges();

        LoadTrips();
    }
}
