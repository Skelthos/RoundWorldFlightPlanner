using RoundWorldFlightPlanner.Core.Enums;
using RoundWorldFlightPlanner.Services;

namespace RoundWorldFlightPlanner.UI.Forms;

/// <summary>Shown once when creating a new itinerary: pick the starting aircraft, country-coverage goal, max flight time, and Antarctica gateway.</summary>
public class TripSetupForm : Form
{
    private readonly ComboBox _aircraftCombo;
    private readonly NumericUpDown _countryGoalPercent;
    private readonly NumericUpDown _maxFlightTimeHours;
    private readonly ComboBox _antarcticaGatewayCombo;
    private readonly CheckBox _includeReservesCheckBox;

    public int SelectedPresetIndex => _aircraftCombo.SelectedIndex;
    public int CountryGoalPercent => (int)_countryGoalPercent.Value;
    public double MaxFlightTimeHours => (double)_maxFlightTimeHours.Value;
    public AntarcticaGateway AntarcticaGateway => _antarcticaGatewayCombo.SelectedIndex == 1
        ? AntarcticaGateway.NewZealandMcMurdo
        : AntarcticaGateway.ChilePeninsula;
    public bool IncludeReserveWaypoints => _includeReservesCheckBox.Checked;

    public TripSetupForm()
    {
        Text = "New Round-the-World Trip";
        Width = 460;
        Height = 370;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16),
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _aircraftCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        foreach (var preset in AircraftPresetLibrary.Presets)
        {
            _aircraftCombo.Items.Add(preset.Name);
        }
        _aircraftCombo.SelectedIndex = 0;

        _countryGoalPercent = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 50, Dock = DockStyle.Fill };
        _maxFlightTimeHours = new NumericUpDown { Minimum = 0.5m, Maximum = 20m, DecimalPlaces = 1, Increment = 0.5m, Value = 3.0m, Dock = DockStyle.Fill };

        _antarcticaGatewayCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _antarcticaGatewayCombo.Items.Add("Chile (Puerto Williams -> King George Island)");
        _antarcticaGatewayCombo.Items.Add("New Zealand (Christchurch -> McMurdo)");
        _antarcticaGatewayCombo.SelectedIndex = 0;

        _includeReservesCheckBox = new CheckBox { Text = "Include theHunter: Call of the Wild reserve stops", Checked = true, Dock = DockStyle.Fill };

        layout.Controls.Add(new Label { Text = "Starting aircraft:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_aircraftCombo, 1, 0);
        layout.Controls.Add(new Label { Text = "Country goal % (per continent):", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(_countryGoalPercent, 1, 1);
        layout.Controls.Add(new Label { Text = "Max flight time per leg (hours):", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_maxFlightTimeHours, 1, 2);
        layout.Controls.Add(new Label { Text = "Antarctica gateway:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        layout.Controls.Add(_antarcticaGatewayCombo, 1, 3);
        layout.Controls.Add(new Label { Text = "Reserve stops:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 4);
        layout.Controls.Add(_includeReservesCheckBox, 1, 4);

        var note = new Label
        {
            Text = "Antarctica is exempt from the percentage goal - one landing there is enough.\r\nOcean/desert crossings are exempt from the max flight time.",
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.DimGray,
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var okButton = new Button { Text = "Start Trip", DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(note);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
