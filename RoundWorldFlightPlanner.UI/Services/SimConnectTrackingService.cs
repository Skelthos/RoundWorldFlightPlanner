using SimConnect.NET;

namespace RoundWorldFlightPlanner.UI.Services;

/// <summary>One polled snapshot of the aircraft's live state from MSFS.</summary>
public record SimAircraftState(
    double Latitude,
    double Longitude,
    double AltitudeFt,
    double IndicatedAirspeedKts,
    double GroundSpeedKts,
    double VerticalSpeedFpm,
    double HeadingDeg,
    bool OnGround,
    bool EngineRunning,
    bool ParkingBrakeSet);

/// <summary>
/// Wraps <see cref="SimConnectClient"/> (SimConnect.NET) to give <c>MainForm</c> a simple
/// connect-once-and-poll surface, instead of every caller needing to know the underlying library's
/// async simvar-request API. Connection lifecycle (initial connect, detecting a drop, retrying) is
/// handled by the library's own built-in auto-reconnect - this class just exposes whether it's
/// currently connected and lets the caller pull a fresh <see cref="SimAircraftState"/> on demand.
/// </summary>
public class SimConnectTrackingService : IAsyncDisposable
{
    private const string BrakeParkingSimVar = "BRAKE PARKING POSITION";

    private readonly SimConnectClient _client = new("Round-the-World Flight Planner")
    {
        AutoReconnectEnabled = true,
        ReconnectDelay = TimeSpan.FromSeconds(5),
        MaxReconnectAttempts = int.MaxValue,
    };

    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// What went wrong on the most recent failed read, tagged with which specific simvar/call failed -
    /// surfaced directly in the UI so a live-sim API mismatch (wrong simvar name/unit, wrong parameter,
    /// etc.) is visible and reportable instead of silently doing nothing every poll.
    /// </summary>
    public string? LastError { get; private set; }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public SimConnectTrackingService()
    {
        _client.ConnectionStatusChanged += (_, e) =>
        {
            if (e.IsConnected)
            {
                Connected?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        };
        _client.ErrorOccurred += (_, e) => LastError = $"SimConnect error ({e.Context}): {e.Error}";
    }

    /// <summary>Attempts an initial connection - safe to call when MSFS isn't running, it just fails silently and lets auto-reconnect keep trying.</summary>
    public async Task TryConnectAsync(IntPtr windowHandle)
    {
        if (_client.IsConnected)
        {
            return;
        }

        try
        {
            await _client.ConnectAsync(windowHandle, 0, 0, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // MSFS likely isn't running yet - the library's own auto-reconnect will keep trying.
            LastError = $"Connect failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Drains all currently-queued SimConnect messages - must be called regularly (e.g. on a UI timer
    /// tick) for connection/data events to actually fire. <c>ProcessNextMessageAsync</c> returns whether
    /// it processed a message, i.e. it handles exactly one message per call - calling it only once per
    /// tick left most of the queue unprocessed whenever more than one message piled up between ticks,
    /// which silently starved the position/motion/engine requests issued in <see cref="GetCurrentStateAsync"/>
    /// of their responses (they'd time out and read back as stale/default values with no exception, since
    /// the request itself succeeded - only the response never arrived before the next Get call started).
    /// </summary>
    public async Task PumpMessagesAsync()
    {
        if (!_client.IsConnected)
        {
            return;
        }

        try
        {
            const int maxMessagesPerTick = 200;
            for (var i = 0; i < maxMessagesPerTick; i++)
            {
                var processedMessage = await _client.ProcessNextMessageAsync(CancellationToken.None);
                if (!processedMessage)
                {
                    break;
                }
            }
        }
        catch
        {
            // A transient read failure here shouldn't crash the poll loop - the next tick tries again.
        }
    }

    /// <summary>
    /// Current aircraft state, or null if not connected or a read failed. Each simvar read is tried
    /// independently and tagged in <see cref="LastError"/> on failure, rather than one try/catch around
    /// everything - a wrong simvar name/unit for just one value (e.g. the parking brake) would otherwise
    /// silently null out the whole snapshot every poll with no way to tell which call was the problem.
    /// </summary>
    public async Task<SimAircraftState?> GetCurrentStateAsync()
    {
        if (!_client.IsConnected)
        {
            return null;
        }

        try
        {
            var position = await _client.Aircraft.GetPositionAsync(0, CancellationToken.None);
            var motion = await _client.Aircraft.GetMotionAsync(0, CancellationToken.None);
            var onGround = await _client.Aircraft.IsOnGroundAsync(0, CancellationToken.None);

            string? subReadError = null;

            var engineRunning = false;
            try
            {
                var engine = await _client.Aircraft.GetEngineAsync(1, 0, CancellationToken.None);
                engineRunning = engine.IsRunning;
            }
            catch (Exception ex)
            {
                subReadError = $"GetEngineAsync failed: {ex.Message}";
            }

            var parkingBrakeSet = false;
            try
            {
                parkingBrakeSet = await _client.SimVars.GetAsync<bool>(BrakeParkingSimVar, "Bool", 0, CancellationToken.None);
            }
            catch (Exception ex)
            {
                subReadError = $"BRAKE PARKING POSITION read failed: {ex.Message}";
            }

            LastError = subReadError;
            return new SimAircraftState(
                position.Latitude,
                position.Longitude,
                position.Altitude,
                motion.IndicatedAirspeed,
                motion.GroundSpeed,
                motion.VerticalSpeed,
                position.TrueHeading,
                onGround,
                engineRunning,
                parkingBrakeSet);
        }
        catch (Exception ex)
        {
            // A single failed read shouldn't tear down the connection - the next poll tick tries again.
            LastError = $"Position/motion read failed: {ex.Message}";
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
