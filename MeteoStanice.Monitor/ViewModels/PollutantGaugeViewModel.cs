using System.Globalization;
using System.Windows.Media;
using MeteoStanice.Data.Entities;
using MeteoStanice.Monitor.Models;

namespace MeteoStanice.Monitor.ViewModels;

/// <summary>
/// Uroven koncentrace vuci orientacnim prahum.
/// </summary>
public enum PollutionLevel
{
    /// <summary>Hodnota chybi.</summary>
    Unknown,

    /// <summary>Pod prahem varovani.</summary>
    Low,

    /// <summary>Mezi prahem varovani a alarmu.</summary>
    Elevated,

    /// <summary>Nad prahem alarmu.</summary>
    High
}

/// <summary>
/// Kulaty ukazatel koncentrace (napr. PM2,5) pro jednu stanici.
/// Oblouk ma barvu veliciny; uroven koncentrace ukazuje barevny stitek (zelena/oranzova/cervena) s textem.
/// Vlastnosti odpovidaji EnvironmentGaugeViewModel, aby oba typy pouzivaly stejnou sablonu RingTemplate.
/// </summary>
public sealed class PollutantGaugeViewModel : ObservableObject
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly Brush DefaultColor = Palette.FromHex("#3949AB", Palette.Normal);

    private readonly PollutantOptions _options;
    private double _value;
    private bool _hasValue;
    private string _valueText = "–";
    private string _statusText = "bez dat";
    private Brush _brush = Palette.Unknown;
    private Brush _statusBrush = Palette.Unknown;
    private Brush _statusBackground = Palette.None;
    private PollutionLevel _level = PollutionLevel.Unknown;

    /// <summary>
    /// Vytvori ukazatel podle konfigurace.
    /// </summary>
    public PollutantGaugeViewModel(PollutantOptions options)
    {
        _options = options;
        LabelBrush = Palette.FromHex(options.Color, DefaultColor);
        TrackBrush = Palette.Tint(LabelBrush);
    }

    /// <summary>Kod veliciny.</summary>
    public string Code => _options.Code;

    /// <summary>Popisek.</summary>
    public string Label => _options.Label;

    /// <summary>Jednotka.</summary>
    public string Unit => _options.Unit;

    /// <summary>Zacatek stupnice.</summary>
    public double Minimum => 0;

    /// <summary>Konec stupnice.</summary>
    public double Maximum => _options.Maximum;

    /// <summary>PM ukazatele maji plynuly oblouk.</summary>
    public int Segments => 0;

    /// <summary>Doplnkovy radek pod popiskem (u PM nepouzit).</summary>
    public string DetailText => string.Empty;

    /// <summary>Barva veliciny (popisek).</summary>
    public Brush LabelBrush { get; }

    /// <summary>Podklad oblouku – svetla varianta barvy veliciny.</summary>
    public Brush TrackBrush { get; }

    /// <summary>Hodnota oblouku (orezana do rozsahu).</summary>
    public double Value { get => _value; private set => SetProperty(ref _value, value); }

    /// <summary>Hodnota je k dispozici.</summary>
    public bool HasValue { get => _hasValue; private set => SetProperty(ref _hasValue, value); }

    /// <summary>Hodnota bez jednotky.</summary>
    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }

    /// <summary>Uroven nebo chyba senzoru.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Barva oblouku.</summary>
    public Brush Brush { get => _brush; private set => SetProperty(ref _brush, value); }

    /// <summary>Barva textu stitku.</summary>
    public Brush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    /// <summary>Pozadi stitku (barva urovne koncentrace).</summary>
    public Brush StatusBackground { get => _statusBackground; private set => SetProperty(ref _statusBackground, value); }

    /// <summary>Uroven koncentrace.</summary>
    public PollutionLevel Level { get => _level; private set => SetProperty(ref _level, value); }

    /// <summary>Tooltip s prahy.</summary>
    public string ToolTipText =>
        $"{_options.Label}: stupnice 0–{Format(_options.Maximum)} {_options.Unit}\n" +
        $"zvýšená ≥ {Format(_options.WarningThreshold)}, vysoká ≥ {Format(_options.AlarmThreshold)} {_options.Unit}\n" +
        "Prahy jsou orientační a nastavitelné v appsettings.json. Hodnota je 10min průměr\n" +
        "z nízkonákladového senzoru bez kalibrace – nelze ji přímo srovnávat s 24h limity WHO/EU.";

    /// <summary>
    /// Aktualizuje ukazatel z mereni a problemu senzoru.
    /// </summary>
    public void Update(Measurement? measurement, IReadOnlyList<SensorProblem> problems)
    {
        SetLevel(measurement);

        // Chyba senzoru SPS30 ma prednost pred hodnocenim koncentrace: hodnota nemusi byt platna.
        (StationHealth? severity, string problemText) = SensorMap.For(problems, _options.Code);
        if (severity is StationHealth.Error or StationHealth.Offline)
        {
            Brush = Palette.Unknown;
            Level = PollutionLevel.Unknown; // neplatna hodnota nespousti alarm znecisteni
            SetStatus("✖ " + problemText, Palette.High, Palette.None);
        }
        else if (severity is StationHealth.Warning)
        {
            SetStatus("⚠ " + problemText, Palette.Elevated, Palette.None);
        }
    }

    private void SetLevel(Measurement? measurement)
    {
        double? raw = measurement is null ? null : Read(measurement, _options.Code);
        if (raw is null || double.IsNaN(raw.Value))
        {
            Value = 0;
            HasValue = false;
            ValueText = "–";
            Brush = Palette.Unknown;
            Level = PollutionLevel.Unknown;
            SetStatus("bez dat", Palette.Unknown, Palette.None);
            return;
        }

        double value = raw.Value;
        Value = Math.Clamp(value, 0, _options.Maximum);
        HasValue = true;
        ValueText = Format(value);
        Brush = LabelBrush;

        if (_options.AlarmThreshold > 0 && value >= _options.AlarmThreshold)
        {
            Level = PollutionLevel.High;
            SetStatus("vysoká", Palette.OnColor, Palette.High);
        }
        else if (_options.WarningThreshold > 0 && value >= _options.WarningThreshold)
        {
            Level = PollutionLevel.Elevated;
            SetStatus("zvýšená", Palette.OnColor, Palette.Elevated);
        }
        else
        {
            Level = PollutionLevel.Low;
            SetStatus("nízká", Palette.OnColor, Palette.Good);
        }
    }

    private void SetStatus(string text, Brush foreground, Brush background)
    {
        StatusText = text;
        StatusBrush = foreground;
        StatusBackground = background;
    }

    private static string Format(double value) => value.ToString(value >= 100 ? "0" : "0.0", Czech);

    private static double? Read(Measurement measurement, string code) => code.ToLowerInvariant() switch
    {
        "pm1" => measurement.Pm1,
        "pm25" => measurement.Pm25,
        "pm4" => measurement.Pm4,
        "pm10" => measurement.Pm10,
        "numberpm05" => measurement.NumberPm05,
        "numberpm1" => measurement.NumberPm1,
        "numberpm25" => measurement.NumberPm25,
        "numberpm4" => measurement.NumberPm4,
        "numberpm10" => measurement.NumberPm10,
        "typicalparticlesize" => measurement.TypicalParticleSize,
        _ => null
    };
}
