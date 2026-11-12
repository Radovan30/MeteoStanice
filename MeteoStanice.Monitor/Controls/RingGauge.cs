using System.Windows;
using System.Windows.Media;

namespace MeteoStanice.Monitor.Controls;

/// <summary>
/// Kulaty (obloukovy) ukazatel: oblouk 270° od leve dolni casti pres horni stranu k prave dolni.
/// Vykresluje se primo v OnRender, nema zadnou zavislost na externi knihovne.
/// </summary>
public sealed class RingGauge : FrameworkElement
{
    private const double StartAngle = 135.0;
    private const double SweepAngle = 270.0;

    /// <summary>Hodnota na zacatku oblouku.</summary>
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0.0);

    /// <summary>Hodnota na konci oblouku.</summary>
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 100.0);

    /// <summary>Zobrazena hodnota.</summary>
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0.0);

    /// <summary>False = vykresli se jen podklad (hodnota chybi).</summary>
    public static readonly DependencyProperty HasValueProperty = Register(nameof(HasValue), false);

    /// <summary>Tloustka oblouku.</summary>
    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 8.0);

    /// <summary>Pocet dilku (0 = plynuly oblouk).</summary>
    public static readonly DependencyProperty SegmentsProperty = Register(nameof(Segments), 0);

    /// <summary>Barva hodnoty.</summary>
    public static readonly DependencyProperty RingBrushProperty = Register<Brush>(nameof(RingBrush), Brushes.SteelBlue);

    /// <summary>Barva podkladu.</summary>
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush>(nameof(TrackBrush), Brushes.LightGray);

    /// <summary>Hodnota na zacatku oblouku.</summary>
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    /// <summary>Hodnota na konci oblouku.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>Zobrazena hodnota.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>False = vykresli se jen podklad.</summary>
    public bool HasValue { get => (bool)GetValue(HasValueProperty); set => SetValue(HasValueProperty, value); }

    /// <summary>Tloustka oblouku.</summary>
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    /// <summary>Pocet dilku (0 = plynuly oblouk).</summary>
    public int Segments { get => (int)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    /// <summary>Barva hodnoty.</summary>
    public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    /// <summary>Barva podkladu.</summary>
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        double thickness = Math.Max(1, Thickness);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= thickness * 2)
        {
            return;
        }

        Point center = new(ActualWidth / 2, ActualHeight / 2);
        double radius = (size - thickness) / 2;
        bool hasValue = HasValue && Maximum > Minimum && !double.IsNaN(Value);
        double fraction = hasValue ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

        if (Segments > 0)
        {
            RenderSegments(drawingContext, center, radius, thickness, fraction);
            return;
        }

        DrawArc(drawingContext, CreatePen(TrackBrush, thickness, PenLineCap.Round), center, radius, StartAngle, SweepAngle);
        if (fraction > 0.002)
        {
            DrawArc(drawingContext, CreatePen(RingBrush, thickness, PenLineCap.Round), center, radius, StartAngle, SweepAngle * fraction);
        }
    }

    private void RenderSegments(DrawingContext drawingContext, Point center, double radius, double thickness, double fraction)
    {
        int count = Math.Clamp(Segments, 2, 60);
        double step = SweepAngle / count;
        double gap = Math.Min(4.0, step * 0.35);
        double segment = step - gap;
        Pen track = CreatePen(TrackBrush, thickness, PenLineCap.Flat);
        Pen value = CreatePen(RingBrush, thickness, PenLineCap.Flat);
        double filledAngle = SweepAngle * fraction;

        for (int index = 0; index < count; index++)
        {
            double start = StartAngle + index * step + gap / 2;
            DrawArc(drawingContext, track, center, radius, start, segment);

            // Dilek se vyplni pomerne, takze i deleny oblouk ukazuje presnou hodnotu.
            double filled = Math.Clamp(filledAngle - (index * step + gap / 2), 0, segment);
            if (filled > 0.05)
            {
                DrawArc(drawingContext, value, center, radius, start, filled);
            }
        }
    }

    private static Pen CreatePen(Brush brush, double thickness, PenLineCap cap)
    {
        Pen pen = new(brush, thickness) { StartLineCap = cap, EndLineCap = cap };
        pen.Freeze();
        return pen;
    }

    private static void DrawArc(DrawingContext drawingContext, Pen pen, Point center, double radius, double startAngle, double sweep)
    {
        Point start = PointOnCircle(center, radius, startAngle);
        Point end = PointOnCircle(center, radius, startAngle + sweep);

        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static DependencyProperty Register<T>(string name, T defaultValue)
        => DependencyProperty.Register(
            name,
            typeof(T),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
