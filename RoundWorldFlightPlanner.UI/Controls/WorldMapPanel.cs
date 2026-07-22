using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.UI.Controls;

/// <summary>Simple equirectangular world map plotting airports and the planned/flown route.</summary>
public class WorldMapPanel : Control
{
    private const float LegHitTestToleranceScreenPx = 6f;

    private IReadOnlyList<Airport> _airports = [];
    private IReadOnlyList<FlightLeg> _legs = [];
    private FlightLeg? _highlightedLeg;
    private IReadOnlyList<(double Latitude, double Longitude)> _flownTrack = [];

    // The visible longitude/latitude window - defaults to the whole world. Zooming just narrows this
    // window; every projection/hit-test below already goes through it via ToPoint/DrawWrappedLine, so
    // nothing else needs to know whether the view is zoomed in or not.
    private double _viewMinLon = -180;
    private double _viewMaxLon = 180;
    private double _viewMinLat = -90;
    private double _viewMaxLat = 90;

    /// <summary>Raised when the user clicks close enough to a leg's line to select it.</summary>
    public event EventHandler<FlightLeg>? LegClicked;

    public WorldMapPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(12, 30, 54);
    }

    public void LoadRoute(IReadOnlyList<Airport> airports, IReadOnlyList<FlightLeg> legs)
    {
        _airports = airports;
        _legs = legs;
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
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var legPen = new Pen(Color.FromArgb(200, 210, 225), 1.8f);
        using var completedPen = new Pen(Color.LimeGreen, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Solid };
        using var highlightPen = new Pen(Color.Magenta, 3.5f);
        using var waypointBrush = new SolidBrush(Color.Orange);
        using var airportBrush = new SolidBrush(Color.FromArgb(90, 150, 210));
        using var highlightBrush = new SolidBrush(Color.Magenta);
        using var labelBrush = new SolidBrush(Color.White);
        using var font = new Font(Font.FontFamily, 7f);

        foreach (var leg in _legs)
        {
            if (leg.DepartureAirport is null || leg.ArrivalAirport is null || leg == _highlightedLeg)
            {
                continue;
            }

            var from = ToPoint(leg.DepartureAirport);
            var to = ToPoint(leg.ArrivalAirport);
            DrawWrappedLine(g, leg.IsComplete ? completedPen : legPen, from, to);
        }

        foreach (var airport in _airports)
        {
            var point = ToPoint(airport);
            var isWaypoint = airport.ReserveName is not null;
            var brush = isWaypoint ? waypointBrush : airportBrush;
            var radius = isWaypoint ? 4f : 2f;

            g.FillEllipse(brush, point.X - radius, point.Y - radius, radius * 2, radius * 2);

            if (isWaypoint)
            {
                g.DrawString(airport.Icao, font, labelBrush, point.X + 5, point.Y - 6);
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
            g.FillEllipse(highlightBrush, from.X - highlightRadius, from.Y - highlightRadius, highlightRadius * 2, highlightRadius * 2);
            g.FillEllipse(highlightBrush, to.X - highlightRadius, to.Y - highlightRadius, highlightRadius * 2, highlightRadius * 2);
            g.DrawString(
                $"{highlighted.DepartureAirport.Icao} -> {highlighted.ArrivalAirport.Icao}",
                font, labelBrush, to.X + 6, to.Y + 4);
        }
    }

    /// <summary>
    /// Draws a leg's line, wrapping around the left/right edges when it crosses the antimeridian
    /// (longitude +/-180) - otherwise a short real-world hop near the dateline (e.g. between Pacific
    /// islands) would render as one long line stretching across almost the whole map.
    /// </summary>
    private void DrawWrappedLine(Graphics g, Pen pen, PointF from, PointF to)
    {
        var worldWidthPx = WorldWidthPx();
        var dx = to.X - from.X;
        if (Math.Abs(dx) <= worldWidthPx / 2f)
        {
            g.DrawLine(pen, from, to);
            return;
        }

        var shift = worldWidthPx * Math.Sign(dx);
        g.DrawLine(pen, from, new PointF(to.X - shift, to.Y));
        g.DrawLine(pen, new PointF(from.X + shift, from.Y), to);
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

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        var leg = FindLegNearPoint(e.Location);
        if (leg is not null)
        {
            LegClicked?.Invoke(this, leg);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = FindLegNearPoint(e.Location) is null ? Cursors.Default : Cursors.Hand;
    }

    /// <summary>
    /// Finds whichever leg's line passes closest to <paramref name="point"/>, within a small screen-pixel
    /// tolerance, so a leg can be selected by clicking anywhere along it rather than needing pixel-perfect
    /// precision on a route with hundreds of short, tightly-packed hops. Accounts for the same antimeridian
    /// wrap <see cref="DrawWrappedLine"/> draws, checking both wrapped segments when a leg crosses it.
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

            var from = ToPoint(leg.DepartureAirport);
            var to = ToPoint(leg.ArrivalAirport);
            var distance = DistanceToWrappedLine(point, from, to);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = leg;
            }
        }

        return closest;
    }

    private float DistanceToWrappedLine(Point point, PointF from, PointF to)
    {
        var worldWidthPx = WorldWidthPx();
        var dx = to.X - from.X;
        if (Math.Abs(dx) <= worldWidthPx / 2f)
        {
            return DistanceToSegment(point, from, to);
        }

        var shift = worldWidthPx * Math.Sign(dx);
        var segment1 = DistanceToSegment(point, from, new PointF(to.X - shift, to.Y));
        var segment2 = DistanceToSegment(point, new PointF(from.X + shift, from.Y), to);
        return Math.Min(segment1, segment2);
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
