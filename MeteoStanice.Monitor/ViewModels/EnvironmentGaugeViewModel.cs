using System.Globalization;
using System.Windows.Media;
using MeteoStanice.Data.Entities;
using MeteoStanice.Monitor.Models;
using MeteoStanice.Monitor.Services;

namespace MeteoStanice.Monitor.ViewModels;

/// <summary>
/// Kulaty ukazatel jedne veliciny prostredi (teplota, vlhkost, tlak, napeti baterie) vcetne chyb senzoru.
/// </summary>
public sealed class EnvironmentGaugeViewModel(EnvironmentGaugeOptions options) : ObservableObject
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private double _value;
    private bool _hasValue;
    private string _valueText = "–";
    private string _detailText = string.Empty;
    private string _statusText = string.Empty;
    private Brush _brush = Palette.Unknown;
    private Brush _statusBrush = Palette.Unknown;
    private string _toolTipText = string.Empty;

    /// <summary>Kod veliciny.</summary>
    public string Code => options.Code;

    /// <summary>Popisek.</summary>
    public string Label => options.Label;

    /// <summary>Jednotka.</summary>
    public string Unit => options.Unit;

    /// <summary>Zacatek stupnice.</summary>
    public double Minimum => options.Minimum;

    /// <summary>Konec stupnice.</summary>
    public double Maximum => options.Maximum;

    /// <summary>Pocet dilku oblouku (0 = plynuly).</summary>
    public int Segments => options.Segments;

    private Brush NormalBrush { get; } = CreateBrush(options.Color) ?? Palette.Normal;

    /// <summary>Barva veliciny (popisek).</summary>
    public Brush LabelBrush => NormalBrush;

    /// <summary>Podklad oblouku – svetla varianta barvy veliciny.</summary>
    public Brush TrackBrush => _trackBrush ??= Palette.Tint(NormalBrush);

    /// <summary>Pozadi stitku (u prostredi bez pozadi).</summary>
    public Brush StatusBackground => Palette.None;

    private Brush? _trackBrush;

    /// <summary>Hodnota pro vykresleni oblouku.</summary>
    public double Value { get => _value; private set => SetProperty(ref _value, value); }

    /// <summary>Hodnota je k dispozici.</summary>
    public bool HasValue { get => _hasValue; private set => SetProperty(ref _hasValue, value); }

    /// <summary>Text hodnoty uprostred.</summary>
    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }

    /// <summary>Min–max v intervalu.</summary>
    public string DetailText { get => _detailText; private set => SetProperty(ref _detailText, value); }

    /// <summary>Chyba nebo upozorneni pod ukazatelem.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Barva oblouku.</summary>
    public Brush Brush { get => _brush; private set => SetProperty(ref _brush, value); }

    /// <summary>Barva textu chyby.</summary>
    public Brush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    /// <summary>Tooltip.</summary>
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }

    /// <summary>
    /// Aktualizuje ukazatel z mereni a problemu senzoru.
    /// </summary>
    public void Update(Measurement? measurement, IReadOnlyList<SensorProblem> problems, double altitudeMeters, PressureReductionOptions reduction)
    {
        bool seaLevel = options.Code.Equals("pressureSeaLevel", StringComparison.OrdinalIgnoreCase);
        double? raw = measurement is null
            ? null
            : seaLevel
                ? PressureReduction.ToSeaLevel(measurement.PressureHpa, measurement.TemperatureCelsius, altitudeMeters, reduction.UseMeasuredTemperature)
                : Read(measurement, options.Code);
        (double? min, double? max) = ReadRange(measurement, options.Code);
        (StationHealth? severity, string problemText) = SensorMap.For(problems, options.Code);

        HasValue = raw is not null && !double.IsNaN(raw.Value);
        Value = HasValue ? Math.Clamp(raw!.Value, options.Minimum, Math.Max(options.Minimum, options.Maximum)) : options.Minimum;
        ValueText = HasValue ? Format(raw!.Value) : "–";
        DetailText = seaLevel
            ? (measurement?.PressureHpa is { } stationPressure
                ? $"stanice {Format(stationPressure)} · {altitudeMeters.ToString("0", Czech)} m"
                : $"{altitudeMeters.ToString("0", Czech)} m n. m.")
            : min is not null && max is not null ? $"{Format(min.Value)} – {Format(max.Value)}" : string.Empty;

        string rangeText = string.Empty;
        if (HasValue && options.WarningLow is { } low && raw!.Value < low)
        {
            rangeText = $"pod {Format(low)} {options.Unit}";
        }
        else if (HasValue && options.WarningHigh is { } high && raw!.Value > high)
        {
            rangeText = $"nad {Format(high)} {options.Unit}";
        }

        if (severity is StationHealth.Error or StationHealth.Offline)
        {
            // Sedy oblouk: hodnota nemusi byt platna (cervena je u teploty bezna barva, chybu nese text).
            Brush = Palette.Unknown;
            StatusBrush = Palette.High;
            StatusText = "✖ " + problemText;
        }
        else if (severity is StationHealth.Warning || rangeText.Length > 0)
        {
            Brush = HasValue ? Palette.Elevated : Palette.Unknown;
            StatusBrush = Palette.Elevated;
            StatusText = "⚠ " + string.Join(", ", new[] { problemText, rangeText }.Where(text => text.Length > 0));
        }
        else
        {
            Brush = HasValue ? NormalBrush : Palette.Unknown;
            StatusBrush = Palette.Unknown;
            StatusText = HasValue ? string.Empty : "bez dat";
        }

        string sensor = SensorMap.SensorFor(options.Code);
        ToolTipText =
            $"{options.Label}{(sensor.Length > 0 ? $" ({sensor})" : string.Empty)}: " +
            $"stupnice {Format(options.Minimum)} – {Format(options.Maximum)} {options.Unit}" +
            (seaLevel
                ? $"\nPřepočet na hladinu moře z tlaku stanice pro {altitudeMeters.ToString("0", Czech)} m n. m. " +
                  (reduction.UseMeasuredTemperature ? "a naměřené teploty" : "a standardní teploty 15 °C") +
                  ".\np₀ = p · exp(g·h / (Rd · Tm)), Tm = T + 273,15 + 0,0065·h/2" +
                  "\nZjednodušená redukce (bez vlhkosti) – od údajů ČHMÚ se může lišit o desetiny hPa."
                : DetailText.Length > 0 ? $"\nmin – max v intervalu: {DetailText} {options.Unit}" : string.Empty) +
            (options.Code.Equals("humidity", StringComparison.OrdinalIgnoreCase)
                ? "\nPři vysoké relativní vlhkosti může optický senzor SPS30 nadhodnocovat koncentrace PM."
                : string.Empty);
    }

    private static Brush? CreateBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex.Trim()));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private string Format(double value) => value.ToString("F" + Math.Clamp(options.Decimals, 0, 4).ToString(CultureInfo.InvariantCulture), Czech);

    private static double? Read(Measurement measurement, string code) => code.ToLowerInvariant() switch
    {
        "temperature" => measurement.TemperatureCelsius,
        "humidity" => measurement.HumidityPercent,
        "pressure" => measurement.PressureHpa,
        "batteryvoltage" => measurement.BatteryVoltage,
        "typicalparticlesize" => measurement.TypicalParticleSize,
        _ => null
    };

    private static (double? Min, double? Max) ReadRange(Measurement? measurement, string code) => code.ToLowerInvariant() switch
    {
        _ when measurement is null => (null, null),
        "temperature" => (measurement.TemperatureMin, measurement.TemperatureMax),
        "humidity" => (measurement.HumidityMin, measurement.HumidityMax),
        "pressure" => (measurement.PressureMin, measurement.PressureMax),
        _ => (null, null)
    };
}
