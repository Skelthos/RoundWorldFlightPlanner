using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;
using RoundWorldFlightPlanner.Services;

namespace RoundWorldFlightPlanner.UI.Forms;

public class LegDetailForm : Form
{
    private readonly FlightLeg _leg;
    private readonly List<CargoItem> _cargo;
    private readonly WeatherService _weatherService;
    private readonly HappinessService _happinessService;
    private readonly Itinerary _itinerary;
    private readonly FlightPlannerDbContext _context;
    private readonly SimBriefService _simBriefService = new(new HttpClient());
    private readonly RunwayPerformanceService _runwayPerformanceService = new();

    private readonly TextBox _weatherBox;
    private readonly CheckedListBox _cargoList;

    private bool _changed;

    public LegDetailForm(
        FlightLeg leg,
        WeightBalanceResult wb,
        List<CargoItem> cargo,
        WeatherService weatherService,
        HappinessService happinessService,
        Itinerary itinerary,
        FlightPlannerDbContext context)
    {
        _leg = leg;
        _cargo = cargo;
        _weatherService = weatherService;
        _happinessService = happinessService;
        _itinerary = itinerary;
        _context = context;

        Text = $"Leg {leg.SequenceNumber}: {leg.DepartureAirport?.Icao} -> {leg.ArrivalAirport?.Icao}";
        Width = 560;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;

        var infoBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            Height = 190,
            ScrollBars = ScrollBars.Vertical,
            Text = BuildSummary(wb),
        };

        var weatherButton = new Button { Text = "Fetch Live Weather (METAR + TAF)", Dock = DockStyle.Top, Height = 32 };
        _weatherBox = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 140, ScrollBars = ScrollBars.Vertical };
        weatherButton.Click += async (_, _) => await FetchWeatherAsync();

        var simBriefButton = new Button { Text = "Pull SimBrief OFP", Dock = DockStyle.Top, Height = 32 };
        simBriefButton.Click += async (_, _) => await PullSimBriefAsync();

        var cargoLabel = new Label { Text = "Cargo manifest (check items to ship home if overweight):", Dock = DockStyle.Top, Height = 24, Padding = new Padding(4, 6, 0, 0) };
        _cargoList = new CheckedListBox { Dock = DockStyle.Fill };
        foreach (var item in _cargo)
        {
            _cargoList.Items.Add($"{item.Description} - {item.WeightKg:F1} kg");
        }

        var shipHomeButton = new Button { Text = "Ship Checked Items Home", Dock = DockStyle.Bottom, Height = 32 };
        shipHomeButton.Click += (_, _) => ShipCheckedItemsHome();

        Controls.Add(_cargoList);
        Controls.Add(shipHomeButton);
        Controls.Add(cargoLabel);
        Controls.Add(_weatherBox);
        Controls.Add(simBriefButton);
        Controls.Add(weatherButton);
        Controls.Add(infoBox);

        FormClosing += (_, _) => DialogResult = _changed ? DialogResult.OK : DialogResult.Cancel;
    }

    private string BuildSummary(WeightBalanceResult wb)
    {
        var lines = new List<string?>
        {
            $"Departure: {_leg.DepartureAirport?.Icao} - {_leg.DepartureAirport?.Name}",
            $"Arrival:   {_leg.ArrivalAirport?.Icao} - {_leg.ArrivalAirport?.Name}",
            $"Reserve:   {_leg.ArrivalAirport?.ReserveName ?? "(not a themed waypoint)"}",
            $"Phase:     {_leg.Phase}",
            _leg.BlockTime.HasValue ? $"Block time: {_leg.BlockTime:h\\:mm}" : "Block time: (not flown yet)",
            _leg.TouchdownVerticalSpeedFpm.HasValue ? $"Touchdown VS: {_leg.TouchdownVerticalSpeedFpm:F0} fpm ({_happinessService.ClassifyLanding(_leg.TouchdownVerticalSpeedFpm.Value)})" : "Touchdown VS: (not landed yet)",
            _leg.LayoverDays > 0 ? $"Layover: {_leg.LayoverDays} day(s)" : null,
            "",
            "-- Weight & Balance (current manifest) --",
            $"Gross weight: {wb.GrossWeightKg:F0} kg / max {wb.MaxGrossWeightKg:F0} kg" + (wb.IsOverweight ? "  *** OVERWEIGHT ***" : ""),
            $"Cargo weight: {wb.CargoWeightKg:F1} kg (of which wife's outfits: {wb.WifeOutfitWeightKg:F1} kg)",
            $"CG position: {wb.CgPositionM:F2} m (limits {wb.CgForwardLimitM:F2} - {wb.CgAftLimitM:F2} m)" + (wb.IsCgInEnvelope ? "" : "  *** OUT OF ENVELOPE ***"),
        };

        if (_leg.SimBriefGeneratedUtc.HasValue)
        {
            lines.Add("");
            lines.Add("-- SimBrief OFP --");
            lines.Add($"Fuel plan: {_leg.SimBriefFuelPlanKg:F0} kg, alternate: {_leg.SimBriefAlternateIcao}, generated {_leg.SimBriefGeneratedUtc:g} UTC");
        }

        if (_itinerary.Aircraft is not null && _leg.DepartureAirport is not null && _leg.ArrivalAirport is not null)
        {
            lines.Add("");
            lines.Add("-- Runway Performance (estimated - POH square-law approximation, not certified) --");
            lines.AddRange(BuildRunwaySummary(_itinerary.Aircraft, wb.GrossWeightKg));
        }

        return string.Join(Environment.NewLine, lines.Where(l => l is not null))!;
    }

    private List<string> BuildRunwaySummary(Aircraft aircraft, double takeoffWeightKg)
    {
        var lines = new List<string>();

        var takeoff = _runwayPerformanceService.EvaluateTakeoff(aircraft, _leg.DepartureAirport!, takeoffWeightKg);
        lines.Add(FormatRunwayLine("Departure", _leg.DepartureAirport!, takeoff, "takeoff"));

        var eteHours = _leg.PlannedCruiseSpeedKts > 0 ? _leg.PlannedDistanceNm / _leg.PlannedCruiseSpeedKts : 0;
        var fuelBurnedKg = eteHours * aircraft.FuelBurnKgPerHour;
        var landingWeightKg = Math.Max(aircraft.EmptyWeightKg, takeoffWeightKg - fuelBurnedKg);
        var landing = _runwayPerformanceService.EvaluateLanding(aircraft, _leg.ArrivalAirport!, landingWeightKg);
        lines.Add(FormatRunwayLine("Arrival", _leg.ArrivalAirport!, landing, "landing"));

        return lines;
    }

    private static string FormatRunwayLine(string label, Airport airport, RunwayPerformanceResult result, string phase)
    {
        if (!result.IsRunwayDataKnown)
        {
            return $"{label} ({airport.Icao}): runway length unknown - can't estimate {phase} performance.";
        }

        var surface = result.IsPaved ? "paved" : "unpaved";
        var verdict = result.IsSufficient ? "OK" : "*** RUNWAY TOO SHORT ***";
        return $"{label} ({airport.Icao}, {result.RunwayLengthFt}ft {surface}): needs ~{result.RequiredDistanceFt:F0}ft at {result.ActualWeightKg:F0}kg - {verdict}. Max weight this runway supports: {result.MaxWeightForRunwayKg:F0}kg.";
    }

    private async Task FetchWeatherAsync()
    {
        _weatherBox.Text = "Fetching...";
        try
        {
            var depIcao = _leg.DepartureAirport?.Icao;
            var arrIcao = _leg.ArrivalAirport?.Icao;

            var depWeather = depIcao is not null ? await _weatherService.GetCurrentWeatherAsync(depIcao) : null;
            var arrWeather = arrIcao is not null ? await _weatherService.GetCurrentWeatherAsync(arrIcao) : null;
            var depTaf = depIcao is not null ? await _weatherService.GetForecastAsync(depIcao) : null;
            var arrTaf = arrIcao is not null ? await _weatherService.GetForecastAsync(arrIcao) : null;

            var lines = new List<string> { "-- Departure METAR --", depWeather?.RawMetar ?? "No current METAR available." };
            lines.Add("-- Departure TAF --");
            lines.Add(depTaf?.RawTaf ?? "No TAF available.");
            lines.Add("");
            lines.Add("-- Arrival METAR --");
            lines.Add(arrWeather?.RawMetar ?? "No current METAR available.");
            lines.Add("-- Arrival TAF --");
            lines.Add(arrTaf?.RawTaf ?? "No TAF available.");

            _weatherBox.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            _weatherBox.Text = $"Weather fetch failed: {ex.Message}";
        }
    }

    private async Task PullSimBriefAsync()
    {
        var settings = _context.AppSettings.FirstOrDefault() ?? new AppSettings();
        using var usernamePrompt = new TextInputPromptForm("SimBrief Username", "SimBrief username / pilot ID:", settings.SimBriefUsername ?? "");
        if (usernamePrompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(usernamePrompt.Value))
        {
            return;
        }

        settings.SimBriefUsername = usernamePrompt.Value;
        if (settings.Id == 0)
        {
            _context.AppSettings.Add(settings);
        }
        _context.SaveChanges();

        try
        {
            var ofp = await _simBriefService.FetchLatestOfpAsync(settings.SimBriefUsername);
            if (ofp is null)
            {
                MessageBox.Show("No SimBrief OFP found for that username. Dispatch a flight on simbrief.com first.");
                return;
            }

            if (!string.Equals(ofp.OriginIcao, _leg.DepartureAirport?.Icao, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ofp.DestinationIcao, _leg.ArrivalAirport?.Icao, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    $"Your latest SimBrief OFP is {ofp.OriginIcao} -> {ofp.DestinationIcao}, but this leg is " +
                    $"{_leg.DepartureAirport?.Icao} -> {_leg.ArrivalAirport?.Icao}. Dispatch the matching flight on SimBrief first.",
                    "OFP mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _leg.SimBriefFuelPlanKg = ofp.PlanRampFuelKg;
            _leg.SimBriefAlternateIcao = ofp.AlternateIcao;
            _leg.SimBriefRouteText = ofp.RouteText;
            _leg.SimBriefGeneratedUtc = ofp.GeneratedUtc;
            _changed = true;

            MessageBox.Show($"Pulled SimBrief OFP: {ofp.PlanRampFuelKg:F0} kg ramp fuel, alternate {ofp.AlternateIcao}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"SimBrief fetch failed: {ex.Message}");
        }
    }

    private void ShipCheckedItemsHome()
    {
        var checkedCount = 0;
        for (var i = 0; i < _cargoList.Items.Count; i++)
        {
            if (!_cargoList.GetItemChecked(i))
            {
                continue;
            }

            _cargo[i].IsShippedHome = true;
            checkedCount++;
        }

        if (checkedCount == 0)
        {
            MessageBox.Show("Check at least one item first.");
            return;
        }

        _happinessService.RecordBaggageOffload(_itinerary, _leg, checkedCount);
        _changed = true;
        MessageBox.Show($"Shipped {checkedCount} item(s) home. She's not thrilled about it.");
        DialogResult = DialogResult.OK;
        Close();
    }
}
