using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SerialTerminal.Core;

namespace SerialTerminal;

/// <summary>Lightweight live line chart for <see cref="GraphData"/> (last N seconds, auto-scaled Y).</summary>
public sealed class ChartControl : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(GraphData), typeof(ChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowSecondsProperty = DependencyProperty.Register(
        nameof(WindowSeconds), typeof(double), typeof(ChartControl),
        new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale), typeof(bool), typeof(ChartControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IncludeZeroProperty = DependencyProperty.Register(
        nameof(IncludeZero), typeof(bool), typeof(ChartControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YMinProperty = DependencyProperty.Register(
        nameof(YMin), typeof(double), typeof(ChartControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YMaxProperty = DependencyProperty.Register(
        nameof(YMax), typeof(double), typeof(ChartControl),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly DispatcherTimer _timer;

    public ChartControl()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start();
            else _timer.Stop();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    public GraphData? Data
    {
        get => (GraphData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double WindowSeconds
    {
        get => (double)GetValue(WindowSecondsProperty);
        set => SetValue(WindowSecondsProperty, value);
    }

    /// <summary>True: Y range follows the visible data. False: fixed <see cref="YMin"/>..<see cref="YMax"/>.</summary>
    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    /// <summary>In auto mode, always keep 0 inside the Y range.</summary>
    public bool IncludeZero
    {
        get => (bool)GetValue(IncludeZeroProperty);
        set => SetValue(IncludeZeroProperty, value);
    }

    public double YMin
    {
        get => (double)GetValue(YMinProperty);
        set => SetValue(YMinProperty, value);
    }

    public double YMax
    {
        get => (double)GetValue(YMaxProperty);
        set => SetValue(YMaxProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        var background = TryFindResource("TermBackground") as Brush ?? Brushes.Black;
        var muted = TryFindResource("TermTimestamp") as Brush ?? Brushes.Gray;
        var text = TryFindResource("TermRx") as Brush ?? Brushes.White;
        dc.DrawRectangle(background, null, new Rect(size));
        if (size.Width < 120 || size.Height < 60) return;

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        FormattedText Label(string s, Brush b) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, b, dip);

        var data = Data;
        if (data == null || data.Series.Count == 0)
        {
            dc.DrawText(Label("No numeric data yet. Received text lines like \"temp=23.5 hum=40\" or \"12 34 56\" are plotted here.", muted),
                new Point(12, 10));
            return;
        }

        double window = Math.Max(1, WindowSeconds);
        double tEnd = data.Now;
        double tStart = tEnd - window;

        var (min, max) = AutoScale ? AutoRange(data, tStart) : (Math.Min(YMin, YMax), Math.Max(YMin, YMax));
        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }

        const double left = 64, right = 16, top = 30, bottom = 24, tick = 4;
        var plot = new Rect(left, top, Math.Max(1, size.Width - left - right), Math.Max(1, size.Height - top - bottom));
        var labelBrush = WithOpacity(text, 0.85);
        var gridPen = MakePen(muted, 0.5, DashStyles.Dot);
        var axisPen = MakePen(WithOpacity(text, 0.6), 1);
        var zeroPen = MakePen(WithOpacity(text, 0.45), 1.2, DashStyles.Dash);

        // Y: round tick values (e.g. -20, 0, 20, 40); in auto mode the range snaps to them
        double step = NiceStep(max - min, 5);
        if (AutoScale)
        {
            min = Math.Floor(min / step) * step;
            max = Math.Ceiling(max / step) * step;
        }
        int decimals = Math.Clamp(-(int)Math.Floor(Math.Log10(step)), 0, 6);
        for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-6; v += step)
        {
            double y = plot.Bottom - (v - min) / (max - min) * plot.Height;
            if (y > plot.Top + 0.5 && y < plot.Bottom - 0.5) dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawLine(axisPen, new Point(plot.Left - tick, y), new Point(plot.Left, y));
            var label = Label(FormatTick(Math.Abs(v) < step * 1e-6 ? 0 : v, decimals), labelBrush);
            dc.DrawText(label, new Point(plot.Left - label.Width - tick - 4, y - label.Height / 2));
        }

        // X: time relative to now
        for (int i = 0; i <= 4; i++)
        {
            double x = plot.Left + plot.Width * i / 4;
            if (i > 0) dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + tick));
            var seconds = (window * (4 - i) / 4).ToString("0.#", CultureInfo.InvariantCulture);
            var timeLabel = Label(i == 4 ? "now" : $"-{seconds} s", labelBrush);
            double tx = Math.Clamp(x - timeLabel.Width / 2, plot.Left, plot.Right - timeLabel.Width);
            dc.DrawText(timeLabel, new Point(tx, plot.Bottom + tick + 2));
        }

        // solid axes: Y on the left, time on the bottom
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        dc.PushClip(new RectangleGeometry(plot));

        // zero line when 0 is inside the range
        if (min < 0 && max > 0)
        {
            double y0 = plot.Bottom - (0 - min) / (max - min) * plot.Height;
            dc.DrawLine(zeroPen, new Point(plot.Left, y0), new Point(plot.Right, y0));
        }

        foreach (var s in data.Series)
        {
            if (s.Points.Count == 0) continue;
            var brush = new SolidColorBrush(s.Color);
            brush.Freeze();
            var pen = new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            Point ToScreen((double T, double V) p) => new(
                plot.Left + (p.T - tStart) / window * plot.Width,
                plot.Bottom - (p.V - min) / (max - min) * plot.Height);

            // start one point before the window so the line enters from the left edge
            int first = Math.Max(0, LowerBound(s.Points, tStart) - 1);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(ToScreen(s.Points[first]), false, false);
                for (int i = first + 1; i < s.Points.Count; i++) ctx.LineTo(ToScreen(s.Points[i]), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
            dc.DrawEllipse(brush, null, ToScreen(s.Points[^1]), 2.5, 2.5);
        }
        dc.Pop();

        // legend
        double lx = plot.Left, ly = 6;
        foreach (var s in data.Series)
        {
            var brush = new SolidColorBrush(s.Color);
            brush.Freeze();
            var label = Label($"{s.Name}: {Format(s.Last)}", text);
            if (lx + label.Width + 20 > size.Width - right && lx > plot.Left) break;
            dc.DrawRectangle(brush, null, new Rect(lx, ly + label.Height / 2 - 1.5, 12, 3));
            dc.DrawText(label, new Point(lx + 16, ly));
            lx += 16 + label.Width + 18;
        }
    }

    /// <summary>1, 2 or 5 times a power of ten, giving about <paramref name="ticks"/> intervals.</summary>
    private static double NiceStep(double range, int ticks)
    {
        double raw = range / ticks;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / magnitude;
        return (n < 1.5 ? 1 : n < 3 ? 2 : n < 7 ? 5 : 10) * magnitude;
    }

    private static string FormatTick(double v, int decimals) =>
        Math.Abs(v) >= 1_000_000 ? v.ToString("G4", CultureInfo.InvariantCulture)
            : v.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        var b = brush.Clone();
        b.Opacity = opacity;
        b.Freeze();
        return b;
    }

    private static Pen MakePen(Brush brush, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(brush, thickness);
        if (dash != null) pen.DashStyle = dash;
        pen.Freeze();
        return pen;
    }

    private (double Min, double Max) AutoRange(GraphData data, double tStart)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in data.Series)
        {
            for (int i = s.Points.Count - 1; i >= 0 && s.Points[i].T >= tStart; i--)
            {
                min = Math.Min(min, s.Points[i].V);
                max = Math.Max(max, s.Points[i].V);
            }
        }
        if (min > max) // nothing in the window - use the last values
        {
            foreach (var s in data.Series.Where(s => s.Points.Count > 0))
            {
                min = Math.Min(min, s.Last);
                max = Math.Max(max, s.Last);
            }
        }
        if (IncludeZero)
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }
        double pad = (max - min) * 0.03;
        // don't push the range below/above zero just for padding
        return (min == 0 ? 0 : min - pad, max == 0 ? 0 : max + pad);
    }

    private static int LowerBound(List<(double T, double V)> points, double t)
    {
        int lo = 0, hi = points.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].T < t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static string Format(double v)
    {
        double abs = Math.Abs(v);
        return abs != 0 && (abs >= 100_000 || abs < 0.01)
            ? v.ToString("G4", CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
