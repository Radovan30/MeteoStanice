using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MeteoStanice.Monitor.Controls;

/// <summary>
/// Jedna krivka grafu (napr. jedna stanice).
/// </summary>
/// <param name="Label">Popisek v legende a v hodnotach pod mysi.</param>
/// <param name="Brush">Barva krivky.</param>
/// <param name="Points">Body serazene podle casu.</param>
public sealed record ChartSeries(string Label, Brush Brush, IReadOnlyList<ChartPoint> Points);

/// <summary>
/// Bod grafu.
/// </summary>
public readonly record struct ChartPoint(DateTimeOffset Time, double Value);

/// <summary>
/// Jednoduchy casovy graf (osa x = mistni cas, osa y = hodnota s jednotkou), vice krivek v jednom grafu.
/// Vykresluje se primo v OnRender bez externi knihovny; mezera v datech delsi nez MaxGap preruší krivku.
/// Pod mysi ukaze cas a hodnoty vsech krivek (barva je vzdy doplnena popiskem).
/// </summary>
public sealed class TimeSeriesChart : FrameworkElement
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)));
    private static readonly Brush TitleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xEC)), 1));
    private static readonly Pen AxisPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC2)), 1));
    private static readonly Pen HoverPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA6)), 1) { DashStyle = DashStyles.Dash });
    private static readonly int[] TimeStepsMinutes = [5, 10, 15, 20, 30, 60, 120, 180, 240, 360];

    private const double LeftMargin = 52;
    private const double RightMargin = 12;
    private const double TopMargin = 24;
    private const double BottomMargin = 22;

    private IReadOnlyList<ChartSeries> _series = [];
    private DateTimeOffset _from = DateTimeOffset.UtcNow.AddHours(-1);
    private DateTimeOffset _to = DateTimeOffset.UtcNow;
    private Point? _hover;

    /// <summary>Nazev grafu (vlevo nahore).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Jednotka osy y.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Pocet desetinnych mist hodnot.</summary>
    public int Decimals { get; set; } = 1;

    /// <summary>Nejmensi rozsah osy y (aby se sum nezvetsil na celou vysku grafu).</summary>
    public double MinimumSpan { get; set; } = 1;

    /// <summary>Spodni mez osy y, pokud ji hodnoty nepresahuji (napr. 0 u PM).</summary>
    public double? Floor { get; set; }

    /// <summary>Mezera v datech, ktera prerusi krivku.</summary>
    public TimeSpan MaxGap { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Nastavi data a prekresli graf.
    /// </summary>
    public void SetData(IReadOnlyList<ChartSeries> series, DateTimeOffset from, DateTimeOffset to)
    {
        _series = series;
        _from = from;
        _to = to;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        dc.DrawText(Text(Title + (Unit.Length > 0 ? $" [{Unit}]" : string.Empty), 12, TitleBrush, true), new Point(4, 2));

        Rect plot = new(LeftMargin, TopMargin, Math.Max(10, width - LeftMargin - RightMargin), Math.Max(10, height - TopMargin - BottomMargin));
        double totalSeconds = Math.Max(1, (_to - _from).TotalSeconds);

        List<double> values = _series.SelectMany(series => series.Points).Select(point => point.Value)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value)).ToList();
        if (values.Count == 0)
        {
            dc.DrawRectangle(null, AxisPen, plot);
            FormattedText empty = Text("bez dat v tomto období", 12, TextBrush);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            DrawTimeAxis(dc, plot, totalSeconds);
            return;
        }

        (double minimum, double maximum, double step) = NiceRange(values.Min(), values.Max());
        double Y(double value) => plot.Bottom - (value - minimum) / (maximum - minimum) * plot.Height;
        double X(DateTimeOffset time) => plot.Left + (time - _from).TotalSeconds / totalSeconds * plot.Width;

        // Mrizka a popisky osy y.
        for (double tick = minimum; tick <= maximum + step / 2; tick += step)
        {
            double y = Y(tick);
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            FormattedText label = Text(Format(tick, step < 1 ? Math.Max(Decimals, 1) : 0), 10, TextBrush);
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
        }

        DrawTimeAxis(dc, plot, totalSeconds);
        dc.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        // Krivky (prerusene pri mezere v datech).
        dc.PushClip(new RectangleGeometry(plot));
        foreach (ChartSeries series in _series)
        {
            Pen pen = new(series.Brush, 1.6) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            StreamGeometry geometry = new();
            using (StreamGeometryContext context = geometry.Open())
            {
                ChartPoint? previous = null;
                foreach (ChartPoint point in series.Points)
                {
                    Point screen = new(X(point.Time), Y(point.Value));
                    if (previous is null || point.Time - previous.Value.Time > MaxGap)
                    {
                        context.BeginFigure(screen, false, false);
                        // Osamoceny bod je jinak neviditelny.
                        context.LineTo(new Point(screen.X + 0.5, screen.Y), true, false);
                    }
                    else
                    {
                        context.LineTo(screen, true, false);
                    }

                    previous = point;
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        dc.Pop();
        DrawHover(dc, plot, totalSeconds);
    }

    private void DrawTimeAxis(DrawingContext dc, Rect plot, double totalSeconds)
    {
        double totalMinutes = totalSeconds / 60.0;
        int stepMinutes = TimeStepsMinutes.FirstOrDefault(step => totalMinutes / step <= 7, 360);
        DateTime localFrom = _from.ToLocalTime().DateTime;
        DateTime first = localFrom.Date.AddMinutes(Math.Ceiling(localFrom.TimeOfDay.TotalMinutes / stepMinutes) * stepMinutes);
        for (DateTime tick = first; tick <= _to.ToLocalTime().DateTime; tick = tick.AddMinutes(stepMinutes))
        {
            DateTimeOffset tickOffset = new(tick, TimeZoneInfo.Local.GetUtcOffset(tick));
            double x = plot.Left + (tickOffset - _from).TotalSeconds / totalSeconds * plot.Width;
            dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            FormattedText label = Text(tick.ToString("HH:mm", Czech), 10, TextBrush);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 4));
        }
    }

    private void DrawHover(DrawingContext dc, Rect plot, double totalSeconds)
    {
        if (_hover is not { } hover || !plot.Contains(hover))
        {
            return;
        }

        DateTimeOffset time = _from.AddSeconds((hover.X - plot.Left) / plot.Width * totalSeconds);
        dc.DrawLine(HoverPen, new Point(hover.X, plot.Top), new Point(hover.X, plot.Bottom));

        double x = Math.Min(hover.X + 8, plot.Right - 150);
        double y = plot.Top + 4;
        FormattedText header = Text(time.ToLocalTime().ToString("HH:mm:ss", Czech), 10, TitleBrush, true);
        List<(FormattedText Text, Brush Brush)> lines = [];
        foreach (ChartSeries series in _series)
        {
            ChartPoint? nearest = Nearest(series.Points, time);
            string value = nearest is { } point ? $"{Format(point.Value, Decimals)} {Unit}" : "–";
            lines.Add((Text($"{series.Label}: {value}", 10, series.Brush, true), series.Brush));
        }

        double boxWidth = Math.Max(header.Width, lines.Count == 0 ? 0 : lines.Max(line => line.Text.Width)) + 12;
        double boxHeight = header.Height + lines.Sum(line => line.Text.Height) + 8;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)), AxisPen, new Rect(x, y, boxWidth, boxHeight), 4, 4);
        dc.DrawText(header, new Point(x + 6, y + 4));
        double lineY = y + 4 + header.Height;
        foreach ((FormattedText text, Brush _) in lines)
        {
            dc.DrawText(text, new Point(x + 6, lineY));
            lineY += text.Height;
        }
    }

    private ChartPoint? Nearest(IReadOnlyList<ChartPoint> points, DateTimeOffset time)
    {
        ChartPoint? best = null;
        double bestDistance = double.MaxValue;
        foreach (ChartPoint point in points)
        {
            double distance = Math.Abs((point.Time - time).TotalSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }

        return bestDistance <= MaxGap.TotalSeconds ? best : null;
    }

    private (double Minimum, double Maximum, double Step) NiceRange(double minimum, double maximum)
    {
        if (Floor is { } floor && minimum >= floor)
        {
            minimum = Math.Min(minimum, floor);
        }

        if (maximum - minimum < MinimumSpan)
        {
            double center = (maximum + minimum) / 2;
            minimum = center - MinimumSpan / 2;
            maximum = center + MinimumSpan / 2;
            if (Floor is { } lower && minimum < lower)
            {
                maximum += lower - minimum;
                minimum = lower;
            }
        }

        double step = NiceStep((maximum - minimum) / 4);
        return (Math.Floor(minimum / step) * step, Math.Ceiling(maximum / step) * step, step);
    }

    private static double NiceStep(double raw)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private static string Format(double value, int decimals)
        => value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Czech);

    private FormattedText Text(string text, double size, Brush brush, bool bold = false)
        => new(
            text,
            Czech,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
