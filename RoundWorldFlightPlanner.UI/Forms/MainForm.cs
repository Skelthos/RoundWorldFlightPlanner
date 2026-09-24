using Microsoft.EntityFrameworkCore;
using RoundWorldFlightPlanner.Core;
using RoundWorldFlightPlanner.Core.Models;
using RoundWorldFlightPlanner.Data;
using RoundWorldFlightPlanner.Services;
using RoundWorldFlightPlanner.UI.Controls;
using RoundWorldFlightPlanner.UI.Services;

namespace RoundWorldFlightPlanner.UI.Forms;

public class MainForm : Form
{
    /// <summary>How close (nm) the aircraft must be to a leg's planned departure airport before an engine start there is trusted to auto-begin that leg - guards against an unrelated engine start (sightseeing, testing) elsewhere.</summary>
    private const double AutoDepartProximityNm = 5;

    /// <summary>How close (nm) a touchdown position must be to a known airport to auto-identify it as the actual landing airport, rather than surfacing it for manual confirmation.</summary>
    private const double LandingIdentificationThresholdNm = 5;

    /// <summary>How often (while airborne) to persist a flown-track position sample.</summary>
    private static readonly TimeSpan TrackSampleInterval = TimeSpan.FromSeconds(15);

    /// <summary>How often to re-pull METAR for the active leg's departure/arrival airports - METAR itself only updates roughly hourly, so this is just to catch a changed report during a long flight, not to poll aggressively.</summary>
    private const int WeatherRefreshIntervalMs = 10 * 60 * 1000;

    private readonly FlightPlannerDbContext _context;
    private readonly GreatCircleService _distanceService = new();
    private readonly WeatherService _weatherService = new(new HttpClient());
    private readonly WeightBalanceService _weightBalanceService = new();
    private readonly PopulationLookupService _populationLookupService = new();
    private readonly WifeShoppingEventService _shoppingEventService;
    private readonly ItineraryContinuityValidator _continuityValidator;
    private readonly HappinessService _happinessService = new();
    private readonly CountryProgressService _countryProgressService = new();
    private readonly RouteAutoFillService _routeAutoFillService;
    private readonly LegSplittingService _legSplittingService;
    private readonly RouteUncrossingService _routeUncrossingService;
    private readonly AirportLookupService _airportLookupService;
    private readonly SimConnectTrackingService _simConnectService = new();
    private readonly System.Windows.Forms.Timer _simConnectTimer = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _weatherTimer = new() { Interval = WeatherRefreshIntervalMs };

    private readonly WorldMapPanel _mapPanel = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _legGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
    // These sit Dock=Bottom directly on the form, whose BackColor swings from green to red with the
    // wife-happiness mood color (RefreshUi/GetContrastingTextColor) - giving them their own fixed dark
    // background keeps the (already-hard-to-read) METAR/telemetry text legible no matter the mood color.
    private static readonly Color StatusStripBackColor = Color.FromArgb(32, 32, 38);

    private readonly Label _statusLabel = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), BackColor = StatusStripBackColor, ForeColor = Color.Gainsboro };
    private readonly Label _happinessLabel = new() { Dock = DockStyle.Bottom, Height = 32, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    private readonly Label _simConnectLabel = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Text = "Simulator: Not connected", BackColor = StatusStripBackColor, ForeColor = Color.Silver };
    private readonly Label _telemetryLabel = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), BackColor = StatusStripBackColor, ForeColor = Color.Silver };
    private readonly Label _departureWeatherLabel = new() { Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 2, 0, 2), BackColor = StatusStripBackColor, ForeColor = Color.PaleTurquoise, Font = new Font(Control.DefaultFont.FontFamily, 8f) };
    private readonly Label _arrivalWeatherLabel = new() { Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 2, 0, 2), BackColor = StatusStripBackColor, ForeColor = Color.PaleTurquoise, Font = new Font(Control.DefaultFont.FontFamily, 8f) };
    private readonly ToolStrip _toolStrip = new() { Renderer = new ToolStripSystemRenderer() };

    private Itinerary? _itinerary;
    private bool _simPollInFlight;
    private bool _weatherRefreshInFlight;
    private double? _lastAirborneVerticalSpeedFpm;
    private DateTime? _lastTrackSampleUtc;
    private int? _weatherFetchedForLegId;

    public MainForm(FlightPlannerDbContext context)
    {
        _context = context;
        _continuityValidator = new ItineraryContinuityValidator(_distanceService);
        _shoppingEventService = new WifeShoppingEventService(_populationLookupService);
        _routeAutoFillService = new RouteAutoFillService(_distanceService);
        _legSplittingService = new LegSplittingService(_distanceService);
        _routeUncrossingService = new RouteUncrossingService(_distanceService);
        _airportLookupService = new AirportLookupService(_distanceService);

        Text = "Round-the-World Flight Planner";
        Width = 1200;
        Height = 800;

        BuildLayout();
        LoadOrSeedItinerary();
        RefreshUi();

        // SimConnect raises these from its own thread, and possibly after the window is already gone.
        _simConnectService.Connected += (_, _) => SetSimulatorStatus("Simulator: Connected");
        _simConnectService.Disconnected += (_, _) => SetSimulatorStatus("Simulator: Not connected");
        _simConnectTimer.Tick += OnSimConnectTimerTick;
        _simConnectTimer.Start();
        _weatherTimer.Tick += async (_, _) =>
        {
            if (!IsDisposed)
            {
                await RefreshWeatherAsync(force: true);
            }
        };
        _weatherTimer.Start();
        // Stop both timers as soon as the window starts closing - otherwise a tick that fires during
        // shutdown touches the disposed form ("Cannot access a disposed object") and the error dialog
        // keeps the process alive.
        FormClosing += (_, _) =>
        {
            _simConnectTimer.Stop();
            _weatherTimer.Stop();
        };
        FormClosed += async (_, _) => await _simConnectService.DisposeAsync();
    }

    private void BuildLayout()
    {
        _toolStrip.Items.Add(new ToolStripButton("Aircraft Setup", null, OnAircraftSetupClicked));
        _toolStrip.Items.Add(new ToolStripButton("Add Leg", null, OnAddLegClicked));
        _toolStrip.Items.Add(new ToolStripButton("Leg Detail", null, OnLegDetailClicked));
        _toolStrip.Items.Add(new ToolStripButton("Progress", null, OnProgressClicked));
        _toolStrip.Items.Add(new ToolStripButton("Manage Trips", null, OnManageTripsClicked));
        _toolStrip.Items.Add(new ToolStripButton("Refactor Route", null, OnRefactorRouteClicked));
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(new ToolStripButton("Depart (Engine Start)", null, OnDepartClicked));
        _toolStrip.Items.Add(new ToolStripButton("Arrive && Shutdown", null, OnArriveClicked));
        _toolStrip.Items.Add(new ToolStripButton("Reset Active Leg", null, OnResetActiveLegClicked));
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(new ToolStripButton("Zoom to Active Leg", null, (_, _) => _mapPanel.ZoomToLeg(GetActiveLeg())));
        _toolStrip.Items.Add(new ToolStripButton("Reset Map View", null, (_, _) => _mapPanel.ResetZoom()));
        var airportsButton = new ToolStripButton("Airports: Route only");
        airportsButton.Click += (_, _) =>
        {
            _mapPanel.AirportDisplay = _mapPanel.AirportDisplay switch
            {
                AirportDisplayMode.RouteOnly => AirportDisplayMode.All,
                AirportDisplayMode.All => AirportDisplayMode.None,
                _ => AirportDisplayMode.RouteOnly,
            };
            airportsButton.Text = _mapPanel.AirportDisplay switch
            {
                AirportDisplayMode.RouteOnly => "Airports: Route only",
                AirportDisplayMode.All => "Airports: All",
                _ => "Airports: Hidden",
            };
        };
        _toolStrip.Items.Add(airportsButton);
        var outlinesButton = new ToolStripButton("Continent outlines") { CheckOnClick = true, Checked = true };
        outlinesButton.CheckedChanged += (_, _) => _mapPanel.ShowLandOutlines = outlinesButton.Checked;
        _toolStrip.Items.Add(outlinesButton);
        _toolStrip.Items.Add(new ToolStripButton("Refresh Weather", null, async (_, _) => await RefreshWeatherAsync(force: true)));

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 720 };
        split.Panel1.Controls.Add(_mapPanel);
        split.Panel2.Controls.Add(_legGrid);

        _legGrid.CellDoubleClick += (_, _) => OnLegDetailClicked(this, EventArgs.Empty);
        _legGrid.SelectionChanged += (_, _) => _mapPanel.SetHighlightedLeg(GetSelectedOrActiveLeg());
        _mapPanel.LegClicked += (_, leg) => SelectLegInGrid(leg);

        Controls.Add(split);
        Controls.Add(_happinessLabel);
        Controls.Add(_telemetryLabel);
        Controls.Add(_arrivalWeatherLabel);
        Controls.Add(_departureWeatherLabel);
        Controls.Add(_simConnectLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_toolStrip);
    }

    private void LoadOrSeedItinerary()
    {
        if (!_context.Itineraries.Any())
        {
            _itinerary = CreateNewItinerary();
            return;
        }

        using var manager = new TripManagerForm(_context, allowCancel: false);
        manager.ShowDialog(this);

        _itinerary = manager.Result switch
        {
            TripManagerForm.ManagerResult.StartNew => CreateNewItinerary(),
            TripManagerForm.ManagerResult.Continue => LoadFullItinerary(manager.SelectedItinerary!.Id),
            _ => _context.Itineraries.Any()
                ? LoadFullItinerary(_context.Itineraries.OrderByDescending(i => i.CreatedUtc).Select(i => i.Id).First())
                : CreateNewItinerary(),
        };
    }

    private Itinerary LoadFullItinerary(int id) => _context.Itineraries
        .Include(i => i.Aircraft)
        .Include(i => i.HappinessEvents)
        .Include(i => i.Legs).ThenInclude(l => l.DepartureAirport)
        .Include(i => i.Legs).ThenInclude(l => l.ArrivalAirport)
        .Include(i => i.Legs).ThenInclude(l => l.TrackPoints)
        .First(i => i.Id == id);

    private void OnManageTripsClicked(object? sender, EventArgs e)
    {
        using var manager = new TripManagerForm(_context, allowCancel: true);
        manager.ShowDialog(this);

        switch (manager.Result)
        {
            case TripManagerForm.ManagerResult.StartNew:
                _itinerary = CreateNewItinerary();
                RefreshUi();
                break;
            case TripManagerForm.ManagerResult.Continue:
                _itinerary = LoadFullItinerary(manager.SelectedItinerary!.Id);
                RefreshUi();
                break;
            default:
                // If the currently loaded trip was deleted while managing, fall back gracefully.
                if (_itinerary is not null && !_context.Itineraries.Any(i => i.Id == _itinerary.Id))
                {
                    _itinerary = _context.Itineraries.Any()
                        ? LoadFullItinerary(_context.Itineraries.OrderByDescending(i => i.CreatedUtc).Select(i => i.Id).First())
                        : CreateNewItinerary();
                    RefreshUi();
                }
                break;
        }
    }

    private void OnRefactorRouteClicked(object? sender, EventArgs e)
    {
        if (_itinerary is null)
        {
            return;
        }

        var beforeCount = _itinerary.Legs.Count;
        var allAirports = _context.Airports.ToList();
        RegenerateRoute(_itinerary, allAirports, resetToSpineFirst: true);
        _context.SaveChanges();
        var afterCount = _itinerary.Legs.Count;

        MessageBox.Show(
            $"Route refactored: {beforeCount} legs -> {afterCount} legs. Not-yet-flown stops were rebuilt from scratch with the current routing logic - already-flown legs were left untouched.",
            "Refactor Route", MessageBoxButtons.OK, MessageBoxIcon.Information);

        RefreshUi();
    }

    private Itinerary CreateNewItinerary()
    {
        using var setupForm = new TripSetupForm();
        var presetIndex = 0;
        var countryGoalPercent = 50;
        var maxFlightTimeHours = 3.0;
        var antarcticaGateway = Core.Enums.AntarcticaGateway.ChilePeninsula;
        var includeReserveWaypoints = true;
        if (setupForm.ShowDialog(this) == DialogResult.OK)
        {
            presetIndex = setupForm.SelectedPresetIndex;
            countryGoalPercent = setupForm.CountryGoalPercent;
            maxFlightTimeHours = setupForm.MaxFlightTimeHours;
            antarcticaGateway = setupForm.AntarcticaGateway;
            includeReserveWaypoints = setupForm.IncludeReserveWaypoints;
        }

        var aircraft = AircraftPresetLibrary.Presets[Math.Max(presetIndex, 0)].Create();
        _context.Aircraft.Add(aircraft);
        _context.SaveChanges();

        var itinerary = ItinerarySeeder.CreateDefaultWestboundItinerary(_context, aircraft, countryGoalPercent, maxFlightTimeHours, antarcticaGateway, includeReserveWaypoints);

        itinerary = _context.Itineraries
            .Include(i => i.Aircraft)
            .Include(i => i.HappinessEvents)
            .Include(i => i.Legs).ThenInclude(l => l.DepartureAirport)
            .Include(i => i.Legs).ThenInclude(l => l.ArrivalAirport)
            .First(i => i.Id == itinerary.Id);

        var allAirports = _context.Airports.ToList();
        RegenerateRoute(itinerary, allAirports, resetToSpineFirst: false);
        _context.SaveChanges();

        return itinerary;
    }

    /// <summary>
    /// The one place the auto-generated part of a route gets (re)built: country-fill, then splitting of
    /// overlong legs, then a global pass that removes any legs left crossing each other on the map.
    /// The uncrossing pass can take a few seconds on a full round-the-world route, hence the wait cursor.
    /// </summary>
    private void RegenerateRoute(Itinerary itinerary, IReadOnlyList<Airport> allAirports, bool resetToSpineFirst)
    {
        var previousCursor = Cursor;
        Cursor = Cursors.WaitCursor;
        try
        {
            if (resetToSpineFirst)
            {
                _routeAutoFillService.ResetToSpine(itinerary);
            }

            _routeAutoFillService.FillToCountryGoal(itinerary, allAirports);
            _legSplittingService.SplitOverlongLegs(itinerary, allAirports);
            _routeUncrossingService.RemoveCrossings(itinerary, allAirports);
        }
        finally
        {
            Cursor = previousCursor;
        }
    }

    private void RefreshUi()
    {
        if (_itinerary is null)
        {
            return;
        }

        var airports = _context.Airports.ToList();
        _mapPanel.LoadRoute(airports, _itinerary.Legs);

        // Show the flown track for whichever leg is most relevant right now - the active one if there
        // is one (even mid-flight, so the track grows live), otherwise the most recently completed leg.
        var trackLeg = GetActiveLeg() ?? _itinerary.Legs.Where(l => l.IsComplete).MaxBy(l => l.SequenceNumber);
        _mapPanel.SetFlownTrack(trackLeg?.TrackPoints
            .OrderBy(t => t.TimestampUtc)
            .Select(t => (t.Latitude, t.Longitude))
            .ToList() ?? []);

        var rows = _itinerary.Legs
            .OrderBy(l => l.SequenceNumber)
            .Select(l =>
            {
                var distanceNm = l.DepartureAirport is not null && l.ArrivalAirport is not null
                    ? _distanceService.GreatCircleDistanceNm(l.DepartureAirport, l.ArrivalAirport)
                    : 0;
                var ete = _itinerary.Aircraft is not null
                    ? _distanceService.EstimateTimeEnroute(distanceNm, _itinerary.Aircraft.CruiseSpeedKts)
                    : TimeSpan.Zero;

                return new
                {
                    l.SequenceNumber,
                    From = l.DepartureAirport?.Icao,
                    To = l.ArrivalAirport?.Icao,
                    Reserve = l.ArrivalAirport?.ReserveName,
                    DistanceNm = Math.Round(distanceNm),
                    Ete = ete.ToString(@"h\:mm"),
                    OceanCrossing = l.IsOceanCrossing ? "Yes" : "",
                    OverTimeLimit = !l.IsOceanCrossing && ete.TotalHours > _itinerary.MaxFlightTimeHours ? "Warning" : "",
                    Status = l.Phase,
                    Takeoff = FormatUtc(l.TakeoffUtc),
                    Landing = FormatUtc(l.LandingUtc),
                    BlockTime = l.BlockTime is { } blockTime ? blockTime.ToString(@"h\:mm") : "",
                    LayoverDays = l.LayoverDays,
                };
            })
            .ToList();

        _legGrid.DataSource = rows;
        static string FormatUtc(DateTime? utc) => utc is { } value ? value.ToString("yyyy-MM-dd HH:mm") + "Z" : "";

        var activeLeg = GetActiveLeg();
        _statusLabel.Text = activeLeg is null
            ? "Itinerary complete - welcome home!"
            : $"Next leg: {activeLeg.DepartureAirport?.Icao} -> {activeLeg.ArrivalAirport?.Icao} ({activeLeg.Phase})";

        _happinessLabel.Text = $"Wife happiness: {_itinerary.HappinessScore}/100"
            + (_happinessService.RequiresForcedShoppingLayover(_itinerary) ? "  *** FORCED SHOPPING LAYOVER NEEDED - she's had enough ***" : "");

        var moodColor = GetHappinessMoodColor(_itinerary.HappinessScore);
        _happinessLabel.BackColor = moodColor;
        _happinessLabel.ForeColor = GetContrastingTextColor(moodColor);
        _toolStrip.BackColor = moodColor;
        BackColor = moodColor;

        _ = RefreshWeatherAsync(force: false);
    }

    /// <summary>
    /// Pulls current METAR for the active leg's departure and arrival airports and shows the raw text.
    /// Only actually re-fetches when <paramref name="force"/> is set or the active leg has changed since
    /// the last fetch - called from every <see cref="RefreshUi"/> pass, so without that guard it would
    /// re-hit the weather API far more often than METAR itself ever changes.
    /// </summary>
    private async Task RefreshWeatherAsync(bool force)
    {
        var leg = GetActiveLeg();
        if (leg?.DepartureAirport is null || leg.ArrivalAirport is null)
        {
            _departureWeatherLabel.Text = "";
            _arrivalWeatherLabel.Text = "";
            return;
        }

        if (!force && _weatherFetchedForLegId == leg.Id)
        {
            return;
        }

        if (_weatherRefreshInFlight)
        {
            return;
        }

        _weatherRefreshInFlight = true;
        try
        {
            _weatherFetchedForLegId = leg.Id;
            var departureIcao = leg.DepartureAirport.Icao;
            var arrivalIcao = leg.ArrivalAirport.Icao;

            var departureWeatherTask = FetchWeatherTextAsync(departureIcao);
            var arrivalWeatherTask = FetchWeatherTextAsync(arrivalIcao);
            await Task.WhenAll(departureWeatherTask, arrivalWeatherTask);

            _departureWeatherLabel.Text = $"DEP {departureIcao}: {departureWeatherTask.Result}";
            _arrivalWeatherLabel.Text = $"ARR {arrivalIcao}: {arrivalWeatherTask.Result}";
        }
        finally
        {
            _weatherRefreshInFlight = false;
        }
    }

    private async Task<string> FetchWeatherTextAsync(string icao)
    {
        try
        {
            var weather = await _weatherService.GetCurrentWeatherAsync(icao);
            return weather is not null && !string.IsNullOrWhiteSpace(weather.RawMetar)
                ? $"{MetarTranslator.ToPlainEnglish(weather)}\n{weather.RawMetar} (fetched {DateTime.UtcNow:HH:mm}Z)"
                : "no current METAR available";
        }
        catch (Exception ex)
        {
            return $"weather fetch failed ({ex.Message})";
        }
    }

    /// <summary>
    /// A continuous red-yellow-green gradient (not just three fixed bands) so the whole screen's mood
    /// visibly shifts with every point of happiness lost or gained, not just when it crosses a
    /// threshold - 0 is a clear angry red, 50 is yellow, 100 is a calm green.
    /// </summary>
    private static Color GetHappinessMoodColor(int happinessScore)
    {
        var clamped = Math.Clamp(happinessScore, 0, 100);
        var (fromColor, toColor, localT) = clamped < 50
            ? (Color.FromArgb(214, 62, 54), Color.FromArgb(230, 196, 46), clamped / 50.0)
            : (Color.FromArgb(230, 196, 46), Color.FromArgb(58, 145, 79), (clamped - 50) / 50.0);

        return Color.FromArgb(
            (int)(fromColor.R + (toColor.R - fromColor.R) * localT),
            (int)(fromColor.G + (toColor.G - fromColor.G) * localT),
            (int)(fromColor.B + (toColor.B - fromColor.B) * localT));
    }

    private static Color GetContrastingTextColor(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6 ? Color.Black : Color.White;
    }

    private FlightLeg? GetActiveLeg() =>
        _itinerary?.Legs.OrderBy(l => l.SequenceNumber).FirstOrDefault(l => !l.IsComplete);

    private void OnAircraftSetupClicked(object? sender, EventArgs e)
    {
        if (_itinerary?.Aircraft is null)
        {
            return;
        }

        using var form = new AircraftSetupForm(_itinerary.Aircraft);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _context.SaveChanges();
            RefreshUi();
        }
    }

    private void OnAddLegClicked(object? sender, EventArgs e)
    {
        if (_itinerary is null)
        {
            return;
        }

        var lastArrival = _itinerary.Legs.OrderByDescending(l => l.SequenceNumber).LastOrDefault()?.ArrivalAirport;
        var homeAirport = _context.Airports.First(a => a.Icao == AirportSeedImporter.HomeIcao);
        var departure = lastArrival ?? homeAirport;

        using var form = new AddLegForm(_context.Airports.ToList(), departure, _itinerary, _distanceService);
        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedDeparture is null || form.SelectedArrival is null)
        {
            return;
        }

        var validation = _continuityValidator.ValidateNewLeg(_itinerary, form.SelectedDeparture, form.SelectedArrival, form.IsOceanCrossing);
        if (!validation.IsValid)
        {
            MessageBox.Show(string.Join(Environment.NewLine, validation.Errors), "Cannot add leg", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (validation.Warnings.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, validation.Warnings), "Heads up", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        var nextSequence = (_itinerary.Legs.Count == 0 ? 0 : _itinerary.Legs.Max(l => l.SequenceNumber)) + 1;
        var leg = new FlightLeg
        {
            ItineraryId = _itinerary.Id,
            SequenceNumber = nextSequence,
            DepartureAirportId = form.SelectedDeparture.Id,
            ArrivalAirportId = form.SelectedArrival.Id,
            PlannedCruiseSpeedKts = _itinerary.Aircraft?.CruiseSpeedKts ?? 0,
            PlannedDistanceNm = _distanceService.GreatCircleDistanceNm(form.SelectedDeparture, form.SelectedArrival),
            IsOceanCrossing = form.IsOceanCrossing,
        };

        _context.FlightLegs.Add(leg);
        _context.SaveChanges();
        _itinerary.Legs.Add(leg);
        leg.DepartureAirport = form.SelectedDeparture;
        leg.ArrivalAirport = form.SelectedArrival;

        RefreshUi();
    }

    private void OnLegDetailClicked(object? sender, EventArgs e)
    {
        var leg = GetSelectedOrActiveLeg();
        if (leg?.DepartureAirport is null || leg.ArrivalAirport is null || _itinerary?.Aircraft is null)
        {
            return;
        }

        var cargo = _context.CargoItems.Where(c => c.AcquiredOnLeg!.ItineraryId == _itinerary.Id && !c.IsShippedHome).ToList();
        var wb = _weightBalanceService.Calculate(_itinerary.Aircraft, _itinerary.Aircraft.FuelCapacityKg, cargo);

        using var form = new LegDetailForm(leg, wb, cargo, _weatherService, _happinessService, _itinerary, _context);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _context.SaveChanges();
            RefreshUi();
        }
    }

    private void OnProgressClicked(object? sender, EventArgs e)
    {
        if (_itinerary is null)
        {
            return;
        }

        var progress = _countryProgressService.GetProgress(_itinerary);
        using var form = new ProgressForm(progress, _itinerary.CountryGoalPercent);
        form.ShowDialog(this);
    }

    /// <summary>Selects the grid row for a leg clicked directly on the map - the existing grid
    /// SelectionChanged handler then re-highlights it, so map-click and grid-click stay in sync.</summary>
    private void SelectLegInGrid(FlightLeg leg)
    {
        foreach (DataGridViewRow row in _legGrid.Rows)
        {
            if (row.Cells["SequenceNumber"].Value is int seq && seq == leg.SequenceNumber)
            {
                _legGrid.ClearSelection();
                row.Selected = true;
                _legGrid.CurrentCell = row.Cells["SequenceNumber"];
                _legGrid.FirstDisplayedScrollingRowIndex = row.Index;
                break;
            }
        }
    }

    private FlightLeg? GetSelectedOrActiveLeg()
    {
        if (_itinerary is null)
        {
            return null;
        }

        if (_legGrid.SelectedRows.Count > 0 && _legGrid.SelectedRows[0].Cells["SequenceNumber"].Value is int seq)
        {
            return _itinerary.Legs.FirstOrDefault(l => l.SequenceNumber == seq);
        }

        return GetActiveLeg();
    }

    private void OnDepartClicked(object? sender, EventArgs e)
    {
        var leg = GetActiveLeg();
        if (leg is null)
        {
            MessageBox.Show("No more legs to fly - the itinerary is complete.");
            return;
        }

        if (leg.Phase != Core.Enums.FlightPhase.NotStarted)
        {
            MessageBox.Show("This leg has already departed.");
            return;
        }

        leg.Phase = Core.Enums.FlightPhase.Airborne;
        leg.EngineStartUtc = DateTime.UtcNow;
        leg.TakeoffUtc = DateTime.UtcNow;
        _context.SaveChanges();
        RefreshUi();
    }

    /// <summary>Manual fallback for when the sim isn't connected - trusts the planned arrival airport and prompts for vertical speed, same as before SimConnect tracking existed.</summary>
    private async void OnArriveClicked(object? sender, EventArgs e)
    {
        var leg = GetActiveLeg();
        if (leg is null || leg.Phase != Core.Enums.FlightPhase.Airborne || _itinerary is null || leg.ArrivalAirport is null)
        {
            MessageBox.Show("No airborne leg to land right now.");
            return;
        }

        using var vsForm = new VerticalSpeedPromptForm();
        if (vsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await FinalizeLegAsync(leg, vsForm.VerticalSpeedFpm, leg.ArrivalAirport);
    }

    /// <summary>
    /// Clears a leg back to <see cref="Core.Enums.FlightPhase.NotStarted"/> - needed because canceling out
    /// of MSFS mid-test (closing the sim, or reloading the flight back at the ramp) leaves the leg's
    /// <c>Phase</c> wherever it was (Airborne/Landed), since nothing else resets it. On the next launch,
    /// the aircraft sits on the ground at the departure airport with a leftover Phase of Airborne - the
    /// auto state machine (<see cref="HandleSimStateAsync"/>) then reads that as "just touched down" and
    /// jumps straight to Landed, without ever re-arming the NotStarted -> Airborne takeoff detection.
    /// </summary>
    private void OnResetActiveLegClicked(object? sender, EventArgs e)
    {
        var leg = GetActiveLeg();
        if (leg is null)
        {
            MessageBox.Show("No active leg to reset - the itinerary is complete.");
            return;
        }

        if (leg.Phase == Core.Enums.FlightPhase.NotStarted)
        {
            MessageBox.Show("This leg hasn't started yet - nothing to reset.");
            return;
        }

        if (MessageBox.Show(
                $"Reset leg {leg.DepartureAirport?.Icao} -> {leg.ArrivalAirport?.Icao} back to Not Started? This clears its recorded times and flown track.",
                "Reset Active Leg", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        leg.Phase = Core.Enums.FlightPhase.NotStarted;
        leg.EngineStartUtc = null;
        leg.TakeoffUtc = null;
        leg.LandingUtc = null;
        leg.ShutDownUtc = null;
        leg.TouchdownVerticalSpeedFpm = null;
        _context.FlightTrackPoints.RemoveRange(_context.FlightTrackPoints.Where(t => t.FlightLegId == leg.Id));
        _lastAirborneVerticalSpeedFpm = null;
        _lastTrackSampleUtc = null;
        _context.SaveChanges();
        _mapPanel.SetFlownTrack([]);
        RefreshUi();
    }

    /// <summary>
    /// Finalizes an airborne leg once it's actually landed - shared by the manual "Arrive &amp; Shutdown"
    /// button (not connected to the sim, trusts the planned arrival airport) and the automatic
    /// SimConnect-driven path (<see cref="HandleSimStateAsync"/>, which passes the real detected
    /// touchdown airport). If <paramref name="actualLandingAirport"/> differs from what was planned,
    /// the leg is corrected to the real airport and the rest of the itinerary is regenerated from there
    /// - the exact same <c>ResetToSpine</c> -&gt; <c>FillToCountryGoal</c> -&gt; <c>SplitOverlongLegs</c>
    /// pipeline "Refactor Route" already uses (<see cref="OnRefactorRouteClicked"/>), just triggered here
    /// automatically and only when a genuine diversion happened.
    /// </summary>
    private async Task FinalizeLegAsync(FlightLeg leg, double touchdownVsFpm, Airport actualLandingAirport)
    {
        if (_itinerary is null)
        {
            return;
        }

        var diverted = actualLandingAirport.Id != leg.ArrivalAirportId;
        if (diverted)
        {
            leg.ArrivalAirportId = actualLandingAirport.Id;
            leg.ArrivalAirport = actualLandingAirport;
            leg.PlannedDistanceNm = _distanceService.GreatCircleDistanceNm(leg.DepartureAirport!, actualLandingAirport);
            leg.IsOceanCrossing = GreatCircleMath.SuggestIsOceanCrossing(leg.DepartureAirport!, actualLandingAirport, leg.PlannedDistanceNm);
        }

        leg.Phase = Core.Enums.FlightPhase.ShutDown;
        leg.LandingUtc ??= DateTime.UtcNow;
        leg.ShutDownUtc = DateTime.UtcNow;
        leg.TouchdownVerticalSpeedFpm = touchdownVsFpm;

        var landingEvent = _happinessService.RecordLanding(_itinerary, leg, touchdownVsFpm);
        var huntingEvent = _happinessService.RecordHuntingTrip(_itinerary, leg);

        var summary = new List<string> { $"Landing: {landingEvent.Reason} ({landingEvent.Delta:+0;-0} happiness)" };
        if (diverted)
        {
            summary.Insert(0, $"Diverted from plan - actually landed at {actualLandingAirport.Icao} ({actualLandingAirport.City}). Route ahead has been regenerated from here.");
        }

        if (huntingEvent is not null)
        {
            summary.Add($"{huntingEvent.Reason} ({huntingEvent.Delta:+0;-0} happiness)");
        }

        try
        {
            var weather = await _weatherService.GetCurrentWeatherAsync(leg.ArrivalAirport!.Icao);
            if (weather is not null)
            {
                var weatherEvent = _happinessService.RecordWeatherPenalty(_itinerary, leg, weather);
                if (weatherEvent is not null)
                {
                    summary.Add($"{weatherEvent.Reason} ({weatherEvent.Delta:+0;-0} happiness)");
                }
            }
        }
        catch
        {
            // Weather pull is a nice-to-have here - don't block finishing the leg if it fails.
        }

        var haul = _shoppingEventService.MaybeGenerateShoppingHaul(leg, leg.ArrivalAirport!);
        if (haul.Count > 0)
        {
            _context.CargoItems.AddRange(haul);
            var shoppingEvent = _happinessService.RecordShoppingHaul(_itinerary, leg, haul);
            summary.Add($"Shopping in {leg.ArrivalAirport!.City}: {haul.Count} outfits, {haul.Sum(h => h.WeightKg):F1} kg ({shoppingEvent.Delta:+0;-0} happiness)");
        }

        _context.SaveChanges();

        if (_happinessService.RequiresForcedShoppingLayover(_itinerary))
        {
            var layoverEvent = _happinessService.ApplyForcedLayover(_itinerary, leg, leg.ArrivalAirport!);
            summary.Add($"{layoverEvent.Reason} ({layoverEvent.Delta:+0;-0} happiness) - 7 day layover added");
            _context.SaveChanges();
        }

        if (diverted)
        {
            var allAirports = _context.Airports.ToList();
            RegenerateRoute(_itinerary, allAirports, resetToSpineFirst: true);
            _context.SaveChanges();
        }

        MessageBox.Show(string.Join(Environment.NewLine, summary), "Arrival Summary", MessageBoxButtons.OK, MessageBoxIcon.Information);

        RefreshUi();
    }

    private void SetSimulatorStatus(string text)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    _simConnectLabel.Text = text;
                }
            });
        }
        catch (InvalidOperationException)
        {
            // window closed between the check and the invoke - nothing left to update
        }
    }

    private async void OnSimConnectTimerTick(object? sender, EventArgs e)
    {
        if (_simPollInFlight || IsDisposed || Disposing)
        {
            return;
        }

        _simPollInFlight = true;
        try
        {
            await _simConnectService.TryConnectAsync(Handle);
            await _simConnectService.PumpMessagesAsync();

            var state = await _simConnectService.GetCurrentStateAsync();
            if (IsDisposed)
            {
                return; // the window closed while we were awaiting the sim
            }

            if (state is not null)
            {
                _telemetryLabel.ForeColor = Color.DimGray;
                _telemetryLabel.Text = $"IAS {state.IndicatedAirspeedKts:F0} kt   GS {state.GroundSpeedKts:F0} kt   ALT {state.AltitudeFt:F0} ft   HDG {state.HeadingDeg:F0}°   VS {state.VerticalSpeedFpm:F0} fpm   {(state.OnGround ? "On ground" : "Airborne")}";
                await HandleSimStateAsync(state);
            }
            else if (_simConnectService.IsConnected)
            {
                // Connected to the sim but a read failed - surface exactly what broke instead of silently doing nothing.
                _telemetryLabel.ForeColor = Color.OrangeRed;
                _telemetryLabel.Text = _simConnectService.LastError ?? "Connected, but the last telemetry read failed.";
            }
            else
            {
                _telemetryLabel.Text = string.Empty;
            }
        }
        finally
        {
            _simPollInFlight = false;
        }
    }

    /// <summary>
    /// Drives the current leg's phase purely off live SimConnect state - no clicks required. Gated to
    /// whichever leg <see cref="GetActiveLeg"/> already considers current, same as the manual buttons.
    /// </summary>
    private async Task HandleSimStateAsync(SimAircraftState state)
    {
        var leg = GetActiveLeg();
        if (leg is null || _itinerary is null)
        {
            return;
        }

        switch (leg.Phase)
        {
            case Core.Enums.FlightPhase.NotStarted:
                if (state.EngineRunning && leg.DepartureAirport is not null && IsNear(state, leg.DepartureAirport, AutoDepartProximityNm))
                {
                    leg.Phase = Core.Enums.FlightPhase.Airborne;
                    leg.EngineStartUtc = DateTime.UtcNow;
                    leg.TakeoffUtc = DateTime.UtcNow;
                    _lastAirborneVerticalSpeedFpm = null;
                    _lastTrackSampleUtc = null;
                    _context.SaveChanges();
                    RefreshUi();
                    _mapPanel.ZoomToLeg(leg);
                }

                break;

            case Core.Enums.FlightPhase.Airborne:
                if (!state.OnGround)
                {
                    _lastAirborneVerticalSpeedFpm = state.VerticalSpeedFpm;
                    RecordTrackPointIfDue(leg, state);
                    break;
                }

                if (_lastAirborneVerticalSpeedFpm is null && leg.DepartureAirport is not null && IsNear(state, leg.DepartureAirport, AutoDepartProximityNm))
                {
                    // Never actually recorded a single airborne sample since Phase became Airborne, and
                    // we're still sitting at the departure airport - this is leftover state from a
                    // canceled/reloaded MSFS session (e.g. quit mid-flight, then relaunched back at the
                    // ramp), not a real touchdown. Re-arm takeoff detection instead of falsely landing.
                    leg.Phase = Core.Enums.FlightPhase.NotStarted;
                    leg.EngineStartUtc = null;
                    leg.TakeoffUtc = null;
                    _context.SaveChanges();
                    RefreshUi();
                    break;
                }

                // Just touched down - move to the intermediate Landed phase and wait for the aircraft
                // to actually stop (engines off, parking brake set) before finalizing. Using the last
                // known airborne vertical speed as the touchdown estimate, since the reading right at
                // ground contact can already reflect gear compression/ground friction.
                leg.Phase = Core.Enums.FlightPhase.Landed;
                leg.LandingUtc = DateTime.UtcNow;
                leg.TouchdownVerticalSpeedFpm = _lastAirborneVerticalSpeedFpm ?? state.VerticalSpeedFpm;
                _context.SaveChanges();
                break;

            case Core.Enums.FlightPhase.Landed:
                if (!state.EngineRunning && state.ParkingBrakeSet)
                {
                    var allAirports = _context.Airports.ToList();
                    var actualAirport = _airportLookupService.FindNearestAirport(state.Latitude, state.Longitude, allAirports, LandingIdentificationThresholdNm);
                    if (actualAirport is null)
                    {
                        MessageBox.Show(
                            "Landed, but no known airport was found within 5nm of the touchdown position - continuing with the planned destination. Use \"Leg Detail\" to correct the arrival airport manually if you actually diverted somewhere else.",
                            "Landing Airport Unknown", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        actualAirport = leg.ArrivalAirport;
                    }

                    if (actualAirport is not null)
                    {
                        await FinalizeLegAsync(leg, leg.TouchdownVerticalSpeedFpm ?? 0, actualAirport);
                    }
                }

                break;
        }
    }

    private void RecordTrackPointIfDue(FlightLeg leg, SimAircraftState state)
    {
        var now = DateTime.UtcNow;
        if (_lastTrackSampleUtc is not null && now - _lastTrackSampleUtc.Value < TrackSampleInterval)
        {
            return;
        }

        _lastTrackSampleUtc = now;
        _context.FlightTrackPoints.Add(new FlightTrackPoint
        {
            FlightLegId = leg.Id,
            TimestampUtc = now,
            Latitude = state.Latitude,
            Longitude = state.Longitude,
            AltitudeFt = state.AltitudeFt,
            GroundSpeedKts = state.GroundSpeedKts,
            VerticalSpeedFpm = state.VerticalSpeedFpm,
            HeadingDeg = state.HeadingDeg,
        });
        _context.SaveChanges();

        _mapPanel.SetFlownTrack(leg.TrackPoints.OrderBy(t => t.TimestampUtc).Select(t => (t.Latitude, t.Longitude)).ToList());

        // ZoomToLeg frames the whole departure-to-arrival span, which barely changes as the flight
        // progresses (both endpoints are fixed) - that's why the auto-follow looked like it wasn't
        // doing anything. CenterOnAircraft recenters on the live position every sample instead, so the
        // view actually tracks the aircraft like a chase cam.
        _mapPanel.CenterOnAircraft(state.Latitude, state.Longitude);
    }

    private bool IsNear(SimAircraftState state, Airport airport, double maxDistanceNm)
    {
        var position = new Airport
        {
            Icao = string.Empty,
            Name = string.Empty,
            City = string.Empty,
            Country = string.Empty,
            ContinentCode = string.Empty,
            Latitude = state.Latitude,
            Longitude = state.Longitude,
        };

        return _distanceService.GreatCircleDistanceNm(position, airport) <= maxDistanceNm;
    }
}
