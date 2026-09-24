using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.UI.Controls;

/// <summary>Which airports the map draws as dots - the full 14,000-airport database buries the route lines.</summary>
public enum AirportDisplayMode
{
    /// <summary>Only airports the route actually uses (default).</summary>
    RouteOnly,

    /// <summary>Every airport in the database.</summary>
    All,

    /// <summary>Just the route lines, plus the highlighted leg's endpoints.</summary>
    None,
}

/// <summary>
/// Simple equirectangular world map plotting the planned/flown route. Mouse wheel zooms toward the
/// cursor, left-drag pans, double-click resets the view; airports are decluttered to the route's own
/// stops by default so the legs stay readable.
/// </summary>
public class WorldMapPanel : Control
{
    private const float LegHitTestToleranceScreenPx = 6f;
    private const float DragThresholdPx = 4f;
    private const double MinLonSpanDeg = 1.0;

    /// <summary>Route stops are labelled with their ICAO once the view is at least this zoomed in.</summary>
    private const double LabelAllStopsBelowLonSpanDeg = 100.0;

    private IReadOnlyList<Airport> _airports = [];
    private IReadOnlyList<Airport> _routeAirports = [];
    private IReadOnlyList<FlightLeg> _legs = [];
    private FlightLeg? _highlightedLeg;
    private IReadOnlyList<(double Latitude, double Longitude)> _flownTrack = [];
    private AirportDisplayMode _airportDisplay = AirportDisplayMode.RouteOnly;
    private bool _showLandOutlines = true;

    // The visible longitude/latitude window - defaults to the whole world. Zooming just narrows this
    // window; every projection/hit-test below already goes through it via ToPoint/Unwrapped, so
    // nothing else needs to know whether the view is zoomed in or not.
    private double _viewMinLon = -180;
    private double _viewMaxLon = 180;
    private double _viewMinLat = -90;
    private double _viewMaxLat = 90;

    private Point _mouseDownPoint;
    private Point _lastDragPoint;
    private bool _mouseIsDown;
    private bool _dragged;

    /// <summary>Raised when the user clicks close enough to a leg's line to select it.</summary>
    public event EventHandler<FlightLeg>? LegClicked;

    public WorldMapPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(12, 30, 54);
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public AirportDisplayMode AirportDisplay
    {
        get => _airportDisplay;
        set
        {
            _airportDisplay = value;
            Invalidate();
        }
    }

    /// <summary>Draws the continent outlines under everything else.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowLandOutlines
    {
        get => _showLandOutlines;
        set
        {
            _showLandOutlines = value;
            Invalidate();
        }
    }

    public void LoadRoute(IReadOnlyList<Airport> airports, IReadOnlyList<FlightLeg> legs)
    {
        _airports = airports;
        _legs = legs;
        _routeAirports = legs
            .SelectMany(l => new[] { l.DepartureAirport, l.ArrivalAirport })
            .Where(a => a is not null)
            .Select(a => a!)
            .DistinctBy(a => a.Id)
            .ToList();
        Invalidate();
    }

    /// <summary>
    /// The actual flown GPS path (recorded <c>FlightTrackPoint</c> samples), drawn as a distinct
    /// polyline on top of the planned straight-line legs - Volanta-style, so the real wandering/curved
    /// path a leg was actually flown along is visible, not just the great-circle line it was planned
    /// against. Pass an empty list to clear it.
    /// </summary>
    public void SetFlownTrack(IReadOnlyList<(double Latitude, double Longitude)> points)
    {
        _flownTrack = points;
        Invalidate();
    }

    /// <summary>Highlights a single leg (e.g. the one selected in the leg grid). Pass null to clear.</summary>
    public void SetHighlightedLeg(FlightLeg? leg)
    {
        _highlightedLeg = leg;
        Invalidate();
    }

    /// <summary>Zooms the view to frame a single leg (plus any flown track already recorded for it), with padding. Pass null to zoom back out to the whole world.</summary>
    public void ZoomToLeg(FlightLeg? leg)
    {
        if (leg?.DepartureAirport is null || leg.ArrivalAirport is null)
        {
            ResetZoom();
            return;
        }

        var lons = new List<double> { leg.DepartureAirport.Longitude, leg.ArrivalAirport.Longitude };
        var lats = new List<double> { leg.DepartureAirport.Latitude, leg.ArrivalAirport.Latitude };
        foreach (var point in _flownTrack)
        {
            lats.Add(point.Latitude);
            lons.Add(point.Longitude);
        }

        // A leg crossing the antimeridian gives a deceptively huge naive longitude span (170 to -170
        // looks like 340 degrees apart, when it's really a short 20-degree hop across the dateline) -
        // unwrap every longitude to within 180 degrees of the first point before taking min/max.
        var referenceLon = lons[0];
        var unwrappedLons = lons.Select(lon =>
            referenceLon - lon > 180 ? lon + 360 :
            referenceLon - lon < -180 ? lon - 360 : lon);

        SetViewBounds(lats.Min(), lats.Max(), unwrappedLons.Min(), unwrappedLons.Max());
    }

    /// <summary>
    /// Centers a tight "chase cam" view directly on the aircraft's current position - unlike
    /// <see cref="ZoomToLeg"/> (which frames the whole departure-to-arrival span and barely changes
    /// as the flight progresses, since both endpoints are fixed), this recenters every call so the
    /// view actually tracks the aircraft as it moves.
    /// </summary>
    public void CenterOnAircraft(double latitude, double longitude, double radiusDeg = 4.0)
    {
        _viewMinLat = Math.Max(latitude - radiusDeg, -90);
        _viewMaxLat = Math.Min(latitude + radiusDeg, 90);
        _viewMinLon = longitude - radiusDeg;
        _viewMaxLon = longitude + radiusDeg;
        NormalizeLongitudeWindow();
        Invalidate();
    }

    /// <summary>Zooms back out to show the whole world.</summary>
    public void ResetZoom()
    {
        _viewMinLon = -180;
        _viewMaxLon = 180;
        _viewMinLat = -90;
        _viewMaxLat = 90;
        Invalidate();
    }

    private void SetViewBounds(double minLat, double maxLat, double minLon, double maxLon)
    {
        // Pad generously so the endpoints aren't jammed against the edge, with a floor so a very short
        // leg doesn't zoom in to an unreadably tiny window.
        var latPad = Math.Max((maxLat - minLat) * 0.3, 3.0);
        var lonPad = Math.Max((maxLon - minLon) * 0.3, 3.0);

        _viewMinLat = Math.Max(minLat - latPad, -90);
        _viewMaxLat = Math.Min(maxLat + latPad, 90);
        _viewMinLon = minLon - lonPad;
        _viewMaxLon = maxLon + lonPad;
        NormalizeLongitudeWindow();
        Invalidate();
    }

    /// <summary>Keeps the longitude window near [-180, 180] so the +/-360 degree wrap copies drawn in <see cref="OnPaint"/> always cover it.</summary>
    private void NormalizeLongitudeWindow()
    {
        if (_viewMinLon > 180)
        {
            _viewMinLon -= 360;
            _viewMaxLon -= 360;
        }
        else if (_viewMaxLon < -180)
        {
            _viewMinLon += 360;
            _viewMaxLon += 360;
        }
    }

    private bool IsZoomedIn => _viewMaxLon - _viewMinLon < 359.9 || _viewMaxLat - _viewMinLat < 179.9;

    // ------------------------------------------------------------------ zoom / pan

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var factor = e.Delta > 0 ? 0.8 : 1.25;
        var fractionX = (double)e.X / Width;
        var fractionY = (double)e.Y / Height;

        var lonSpan = _viewMaxLon - _viewMinLon;
        var latSpan = _viewMaxLat - _viewMinLat;
        var lonUnderCursor = _viewMinLon + fractionX * lonSpan;
        var latUnderCursor = _viewMaxLat - fractionY * latSpan;

        var newLonSpan = Math.Clamp(lonSpan * factor, MinLonSpanDeg, 360);
        var newLatSpan = Math.Clamp(latSpan * factor, MinLonSpanDeg / 2, 180);
        if (newLonSpan >= 360 && newLatSpan >= 180)
        {
            ResetZoom();
            return;
        }

        // Keep the point under the cursor fixed on screen.
        _viewMinLon = lonUnderCursor - fractionX * newLonSpan;
        _viewMaxLon = _viewMinLon + newLonSpan;
        _viewMaxLat = latUnderCursor + fractionY * newLatSpan;
        _viewMinLat = _viewMaxLat - newLatSpan;
        ClampLatitudeWindow();
        NormalizeLongitudeWindow();
        Invalidate();
    }

    private void ClampLatitudeWindow()
    {
        var span = _viewMaxLat - _viewMinLat;
        if (_viewMaxLat > 90)
        {
            _viewMaxLat = 90;
            _viewMinLat = 90 - span;
        }

        if (_viewMinLat < -90)
        {
            _viewMinLat = -90;
            _viewMaxLat = -90 + span;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus(); // a Control only receives mouse-wheel input while focused
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _mouseIsDown = true;
            _dragged = false;
            _mouseDownPoint = e.Location;
            _lastDragPoint = e.Location;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _mouseIsDown = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ResetZoom();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_mouseIsDown && (_dragged || Distance(e.Location, _mouseDownPoint) > DragThresholdPx))
        {
            _dragged = true;
            if (IsZoomedIn && Width > 0 && Height > 0)
            {
                var lonSpan = _viewMaxLon - _viewMinLon;
                var latSpan = _viewMaxLat - _viewMinLat;
                var dLon = (e.X - _lastDragPoint.X) / (double)Width * lonSpan;
                var dLat = (e.Y - _lastDragPoint.Y) / (double)Height * latSpan;
                _viewMinLon -= dLon;
                _viewMaxLon -= dLon;
                _viewMinLat += dLat;
                _viewMaxLat += dLat;
                ClampLatitudeWindow();
                NormalizeLongitudeWindow();
                Invalidate();
            }

            _lastDragPoint = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        Cursor = FindLegNearPoint(e.Location) is null ? Cursors.Default : Cursors.Hand;
    }

    private static float Distance(Point a, Point b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_dragged)
        {
            return; // that was a pan, not a click
        }

        var leg = FindLegNearPoint(e.Location);
        if (leg is not null)
        {
            LegClicked?.Invoke(this, leg);
        }
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var legPen = new Pen(Color.FromArgb(200, 210, 225), 1.8f);
        using var completedPen = new Pen(Color.LimeGreen, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Solid };
        using var highlightPen = new Pen(Color.Magenta, 3.5f);
        using var waypointBrush = new SolidBrush(Color.Orange);
        using var stopBrush = new SolidBrush(Color.FromArgb(230, 240, 255));
        using var airportBrush = new SolidBrush(Color.FromArgb(90, 150, 210));
        using var highlightBrush = new SolidBrush(Color.Magenta);
        using var labelBrush = new SolidBrush(Color.White);
        using var stopLabelBrush = new SolidBrush(Color.FromArgb(200, 215, 235));
        using var hintBrush = new SolidBrush(Color.FromArgb(120, 150, 180));
        using var font = new Font(Font.FontFamily, 7f);

        var worldWidthPx = WorldWidthPx();
        var lonSpan = _viewMaxLon - _viewMinLon;

        if (_showLandOutlines)
        {
            DrawLand(g, worldWidthPx);
        }

        // "All airports" is the clutter the user wants gone by default; it's drawn first, dimmest, and
        // only what falls inside the current view.
        if (_airportDisplay == AirportDisplayMode.All)
        {
            foreach (var airport in _airports)
            {
                foreach (var point in Copies(ToPoint(airport), worldWidthPx))
                {
                    g.FillEllipse(airportBrush, point.X - 1.5f, point.Y - 1.5f, 3f, 3f);
                }
            }
        }

        foreach (var leg in _legs)
        {
            if (leg.DepartureAirport is null || leg.ArrivalAirport is null || leg == _highlightedLeg)
            {
                continue;
            }

            DrawWrappedLine(g, leg.IsComplete ? completedPen : legPen, ToPoint(leg.DepartureAirport), ToPoint(leg.ArrivalAirport));
        }

        if (_airportDisplay != AirportDisplayMode.None)
        {
            var labelEveryStop = lonSpan <= LabelAllStopsBelowLonSpanDeg;
            foreach (var airport in _routeAirports)
            {
                var isWaypoint = airport.ReserveName is not null;
                var radius = isWaypoint ? 4f : 2.5f;
                foreach (var point in Copies(ToPoint(airport), worldWidthPx))
                {
                    g.FillEllipse(isWaypoint ? waypointBrush : stopBrush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                    if (isWaypoint || labelEveryStop)
                    {
                        g.DrawString(airport.Icao, font, isWaypoint ? labelBrush : stopLabelBrush, point.X + 5, point.Y - 6);
                    }
                }
            }
        }

        if (_flownTrack.Count >= 2)
        {
            using var trackPen = new Pen(Color.Cyan, 1.5f);
            for (var i = 0; i < _flownTrack.Count - 1; i++)
            {
                var from = ToPoint(_flownTrack[i].Longitude, _flownTrack[i].Latitude);
                var to = ToPoint(_flownTrack[i + 1].Longitude, _flownTrack[i + 1].Latitude);
                DrawWrappedLine(g, trackPen, from, to);
            }
        }

        // Draw the highlighted leg last so it renders on top of everything else.
        if (_highlightedLeg is { DepartureAirport: not null, ArrivalAirport: not null } highlighted)
        {
            var from = ToPoint(highlighted.DepartureAirport);
            var to = ToPoint(highlighted.ArrivalAirport);
            DrawWrappedLine(g, highlightPen, from, to);

            const float highlightRadius = 6f;
            foreach (var point in Copies(from, worldWidthPx))
            {
                g.FillEllipse(highlightBrush, point.X - highlightRadius, point.Y - highlightRadius, highlightRadius * 2, highlightRadius * 2);
            }

            foreach (var point in Copies(to, worldWidthPx))
            {
                g.FillEllipse(highlightBrush, point.X - highlightRadius, point.Y - highlightRadius, highlightRadius * 2, highlightRadius * 2);
                g.DrawString($"{highlighted.DepartureAirport.Icao} -> {highlighted.ArrivalAirport.Icao}", font, labelBrush, point.X + 6, point.Y + 4);
            }
        }

        g.DrawString("Wheel: zoom   Drag: pan   Double-click: reset", font, hintBrush, 6, Height - 16);
    }

    private void DrawLand(Graphics g, float worldWidthPx)
    {
        using var fill = new SolidBrush(Color.FromArgb(26, 52, 84));
        using var outline = new Pen(Color.FromArgb(70, 110, 150), 1f);

        foreach (var ring in LandOutlines.All)
        {
            var basePoints = new PointF[ring.Length];
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            for (var i = 0; i < ring.Length; i++)
            {
                basePoints[i] = ToPoint(ring[i].Lon, ring[i].Lat);
                minX = Math.Min(minX, basePoints[i].X);
                maxX = Math.Max(maxX, basePoints[i].X);
            }

            foreach (var shift in new[] { -1, 0, 1 })
            {
                if (maxX + shift * worldWidthPx < 0 || minX + shift * worldWidthPx > Width)
                {
                    continue; // this copy is entirely off screen
                }

                var points = shift == 0
                    ? basePoints
                    : basePoints.Select(p => new PointF(p.X + shift * worldWidthPx, p.Y)).ToArray();
                g.FillPolygon(fill, points);
                g.DrawPolygon(outline, points);
            }
        }
    }

    /// <summary>The point itself plus its +/-360 degree wrapped copies that land inside the control - so panning across the dateline still shows everything.</summary>
    private IEnumerable<PointF> Copies(PointF point, float worldWidthPx)
    {
        foreach (var shift in new[] { -1, 0, 1 })
        {
            var x = point.X + shift * worldWidthPx;
            if (x >= -12 && x <= Width + 12 && point.Y >= -12 && point.Y <= Height + 12)
            {
                yield return new PointF(x, point.Y);
            }
        }
    }

    /// <summary>
    /// The on-screen line segments for one leg: its endpoint is unwrapped to sit within half a world of
    /// its start (so a short hop across the antimeridian is a short line, not one spanning the map), then
    /// the result is repeated at -/+ one world width so it also shows on whichever side the view has
    /// panned to.
    /// </summary>
    private IEnumerable<(PointF A, PointF B)> Unwrapped(PointF from, PointF to)
    {
        var worldWidthPx = WorldWidthPx();
        var dx = to.X - from.X;
        var unwrappedTo = Math.Abs(dx) <= worldWidthPx / 2f ? to : new PointF(to.X - worldWidthPx * Math.Sign(dx), to.Y);

        foreach (var shift in new[] { -1, 0, 1 })
        {
            var a = new PointF(from.X + shift * worldWidthPx, from.Y);
            var b = new PointF(unwrappedTo.X + shift * worldWidthPx, unwrappedTo.Y);
            if (Math.Max(a.X, b.X) < 0 || Math.Min(a.X, b.X) > Width)
            {
                continue;
            }

            yield return (a, b);
        }
    }

    /// <summary>Draws a leg's line (see <see cref="Unwrapped"/> for the antimeridian handling).</summary>
    private void DrawWrappedLine(Graphics g, Pen pen, PointF from, PointF to)
    {
        foreach (var (a, b) in Unwrapped(from, to))
        {
            g.DrawLine(pen, a, b);
        }
    }

    /// <summary>Pixel width corresponding to one full 360-degree wrap of longitude at the current zoom level - equals <see cref="Width"/> exactly when showing the whole world.</summary>
    private float WorldWidthPx() => (float)(360.0 / (_viewMaxLon - _viewMinLon) * Width);

    private PointF ToPoint(Airport airport) => ToPoint(airport.Longitude, airport.Latitude);

    private PointF ToPoint(double longitude, double latitude)
    {
        var x = (float)((longitude - _viewMinLon) / (_viewMaxLon - _viewMinLon) * Width);
        var y = (float)((_viewMaxLat - latitude) / (_viewMaxLat - _viewMinLat) * Height);
        return new PointF(x, y);
    }

    /// <summary>
    /// Finds whichever leg's line passes closest to <paramref name="point"/>, within a small screen-pixel
    /// tolerance, so a leg can be selected by clicking anywhere along it rather than needing pixel-perfect
    /// precision on a route with hundreds of short, tightly-packed hops. Checks the same unwrapped
    /// segments <see cref="DrawWrappedLine"/> draws.
    /// </summary>
    private FlightLeg? FindLegNearPoint(Point point)
    {
        FlightLeg? closest = null;
        var closestDistance = LegHitTestToleranceScreenPx;

        foreach (var leg in _legs)
        {
            if (leg.DepartureAirport is null || leg.ArrivalAirport is null)
            {
                continue;
            }

            foreach (var (a, b) in Unwrapped(ToPoint(leg.DepartureAirport), ToPoint(leg.ArrivalAirport)))
            {
                var distance = DistanceToSegment(point, a, b);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = leg;
                }
            }
        }

        return closest;
    }

    private static float DistanceToSegment(Point point, PointF a, PointF b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lengthSquared = abx * abx + aby * aby;

        var t = lengthSquared <= 0f ? 0f : ((point.X - a.X) * abx + (point.Y - a.Y) * aby) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);

        var closestX = a.X + t * abx;
        var closestY = a.Y + t * aby;
        var dx = point.X - closestX;
        var dy = point.Y - closestY;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}
