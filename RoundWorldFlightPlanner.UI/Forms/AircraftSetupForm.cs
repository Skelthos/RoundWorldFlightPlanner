using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Services;

namespace RoundWorldFlightPlanner.UI.Forms;

public class AircraftSetupForm : Form
{
    private readonly Aircraft _aircraft;
    private readonly ComboBox _presetCombo;
    private readonly TextBox _nameBox;
    private readonly NumericUpDown _emptyWeight, _emptyArm, _maxGross, _fuelCapacity, _fuelBurn, _fuelArm,
        _pilotPax, _pilotPaxArm, _cargoArm, _cruiseSpeed, _cgForward, _cgAft, _takeoffDistance, _landingDistance;

    public AircraftSetupForm(Aircraft aircraft)
    {
        _aircraft = aircraft;
        Text = "Aircraft Setup";
        Width = 420;
        Height = 600;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        _presetCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        foreach (var preset in AircraftPresetLibrary.Presets)
        {
            _presetCombo.Items.Add(preset.Name);
        }
        _presetCombo.SelectedIndexChanged += (_, _) => ApplyPreset(_presetCombo.SelectedIndex);

        _nameBox = new TextBox { Text = aircraft.Name, Dock = DockStyle.Fill };
        _emptyWeight = MakeNumeric(aircraft.EmptyWeightKg, 0, 20000);
        _emptyArm = MakeNumeric(aircraft.EmptyWeightArmM, -10, 10, 0.01m);
        _maxGross = MakeNumeric(aircraft.MaxGrossWeightKg, 0, 50000);
        _fuelCapacity = MakeNumeric(aircraft.FuelCapacityKg, 0, 20000);
        _fuelBurn = MakeNumeric(aircraft.FuelBurnKgPerHour, 0, 5000);
        _fuelArm = MakeNumeric(aircraft.FuelArmM, -10, 10, 0.01m);
        _pilotPax = MakeNumeric(aircraft.PilotAndPaxWeightKg, 0, 2000);
        _pilotPaxArm = MakeNumeric(aircraft.PilotAndPaxArmM, -10, 10, 0.01m);
        _cargoArm = MakeNumeric(aircraft.CargoArmM, -10, 10, 0.01m);
        _cruiseSpeed = MakeNumeric(aircraft.CruiseSpeedKts, 1, 1000);
        _cgForward = MakeNumeric(aircraft.CgForwardLimitM, -10, 10, 0.01m);
        _cgAft = MakeNumeric(aircraft.CgAftLimitM, -10, 10, 0.01m);
        _takeoffDistance = MakeNumeric(aircraft.TakeoffDistanceFtAtMaxGrossWeight, 0, 20000);
        _landingDistance = MakeNumeric(aircraft.LandingDistanceFtAtMaxGrossWeight, 0, 20000);

        AddRow(layout, "Load preset", _presetCombo);
        AddRow(layout, "Name", _nameBox);
        AddRow(layout, "Empty weight (kg)", _emptyWeight);
        AddRow(layout, "Empty weight arm (m)", _emptyArm);
        AddRow(layout, "Max gross weight (kg)", _maxGross);
        AddRow(layout, "Fuel capacity (kg)", _fuelCapacity);
        AddRow(layout, "Fuel burn (kg/hr)", _fuelBurn);
        AddRow(layout, "Fuel arm (m)", _fuelArm);
        AddRow(layout, "Pilot + pax weight (kg)", _pilotPax);
        AddRow(layout, "Pilot + pax arm (m)", _pilotPaxArm);
        AddRow(layout, "Cargo arm (m)", _cargoArm);
        AddRow(layout, "Cruise speed (kts)", _cruiseSpeed);
        AddRow(layout, "CG forward limit (m)", _cgForward);
        AddRow(layout, "CG aft limit (m)", _cgAft);
        AddRow(layout, "POH takeoff distance @ max gross (ft)", _takeoffDistance);
        AddRow(layout, "POH landing distance @ max gross (ft)", _landingDistance);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        okButton.Click += (_, _) => SaveToAircraft();
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private static NumericUpDown MakeNumeric(double value, decimal min, decimal max, decimal increment = 1m) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = increment < 1 ? 2 : 0,
        Increment = increment,
        Value = Math.Clamp((decimal)value, min, max),
        Dock = DockStyle.Fill,
    };

    private static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        layout.RowCount++;
        layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, layout.RowCount - 1);
        layout.Controls.Add(control, 1, layout.RowCount - 1);
    }

    private void ApplyPreset(int index)
    {
        if (index < 0 || index >= AircraftPresetLibrary.Presets.Count)
        {
            return;
        }

        var preset = AircraftPresetLibrary.Presets[index].Create();
        _nameBox.Text = preset.Name;
        _emptyWeight.Value = (decimal)preset.EmptyWeightKg;
        _emptyArm.Value = (decimal)preset.EmptyWeightArmM;
        _maxGross.Value = (decimal)preset.MaxGrossWeightKg;
        _fuelCapacity.Value = (decimal)preset.FuelCapacityKg;
        _fuelBurn.Value = (decimal)preset.FuelBurnKgPerHour;
        _fuelArm.Value = (decimal)preset.FuelArmM;
        _pilotPax.Value = (decimal)preset.PilotAndPaxWeightKg;
        _pilotPaxArm.Value = (decimal)preset.PilotAndPaxArmM;
        _cargoArm.Value = (decimal)preset.CargoArmM;
        _cruiseSpeed.Value = (decimal)preset.CruiseSpeedKts;
        _cgForward.Value = (decimal)preset.CgForwardLimitM;
        _cgAft.Value = (decimal)preset.CgAftLimitM;
        _takeoffDistance.Value = (decimal)preset.TakeoffDistanceFtAtMaxGrossWeight;
        _landingDistance.Value = (decimal)preset.LandingDistanceFtAtMaxGrossWeight;
    }

    private void SaveToAircraft()
    {
        _aircraft.Name = _nameBox.Text;
        _aircraft.EmptyWeightKg = (double)_emptyWeight.Value;
        _aircraft.EmptyWeightArmM = (double)_emptyArm.Value;
        _aircraft.MaxGrossWeightKg = (double)_maxGross.Value;
        _aircraft.FuelCapacityKg = (double)_fuelCapacity.Value;
        _aircraft.FuelBurnKgPerHour = (double)_fuelBurn.Value;
        _aircraft.FuelArmM = (double)_fuelArm.Value;
        _aircraft.PilotAndPaxWeightKg = (double)_pilotPax.Value;
        _aircraft.PilotAndPaxArmM = (double)_pilotPaxArm.Value;
        _aircraft.CargoArmM = (double)_cargoArm.Value;
        _aircraft.CruiseSpeedKts = (double)_cruiseSpeed.Value;
        _aircraft.CgForwardLimitM = (double)_cgForward.Value;
        _aircraft.CgAftLimitM = (double)_cgAft.Value;
        _aircraft.TakeoffDistanceFtAtMaxGrossWeight = (double)_takeoffDistance.Value;
        _aircraft.LandingDistanceFtAtMaxGrossWeight = (double)_landingDistance.Value;
    }
}
