using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using MeteoStanice.Data.Entities;
using MeteoStanice.Monitor.Models;
using MeteoStanice.Monitor.Services;

namespace MeteoStanice.Monitor.ViewModels;

/// <summary>
/// Jeden zobrazeny problem stanice.
/// </summary>
/// <param name="Icon">Textova ikona (nezavisla na barve).</param>
/// <param name="Message">Popis.</param>
/// <param name="Brush">Barva.</param>
public sealed record IssueViewModel(string Icon, string Message, Brush Brush);

/// <summary>
/// Karta jedne meteostanice: kulate ukazatele PM a prostredi + stav.
/// </summary>
public sealed class StationViewModel : ObservableObject
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _healthText = string.Empty;
    private Brush _healthBrush = Palette.Unknown;
    private string _footerText = string.Empty;
    private double _gaugeOpacity = 1.0;
    private string _staleText = string.Empty;
    private IReadOnlyList<IssueViewModel> _issues = [];
    private Brush _accentBrush = Palette.Normal;
    private Brush _accentTint = Palette.Tint(Palette.Normal, 0x18);
    private Brush _accentChip = Palette.Tint(Palette.Normal, 0x33);
    private Brush _accentText = Palette.Darken(Palette.Normal);
    private bool _isVisible = true;
    private readonly PressureReductionOptions _pressureReduction;

    /// <summary>
    /// Vytvori kartu se sadou progressbaru podle konfigurace.
    /// </summary>
    public StationViewModel(Guid stationId, IEnumerable<PollutantOptions> pollutants, IEnumerable<EnvironmentGaugeOptions> environmentGauges, PressureReductionOptions pressureReduction)
    {
        StationId = stationId;
        _pressureReduction = pressureReduction;
        Gauges = new ObservableCollection<PollutantGaugeViewModel>(pollutants.Select(item => new PollutantGaugeViewModel(item)));
        EnvironmentGauges = new ObservableCollection<EnvironmentGaugeViewModel>(environmentGauges.Select(item => new EnvironmentGaugeViewModel(item)));
    }

    /// <summary>Databazove Id stanice.</summary>
    public Guid StationId { get; }

    /// <summary>Kod stanice pro razeni.</summary>
    public string SortKey { get; private set; } = string.Empty;

    /// <summary>Progressbary znecisteni.</summary>
    public ObservableCollection<PollutantGaugeViewModel> Gauges { get; }

    /// <summary>Kulate ukazatele prostredi.</summary>
    public ObservableCollection<EnvironmentGaugeViewModel> EnvironmentGauges { get; }

    /// <summary>Nazev stanice.</summary>
    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(ShortTitle));
            }
        }
    }

    /// <summary>
    /// Kratky nazev pro prepinac: "meteostanice-001" -> "Stanice 1"; jinak cely nazev.
    /// </summary>
    public string ShortTitle
    {
        get
        {
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(_title, @"(\d+)\s*$");
            return match.Success && int.TryParse(match.Groups[1].Value, out int number)
                ? $"Stanice {number}"
                : _title;
        }
    }

    /// <summary>Kod a umisteni.</summary>
    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }

    /// <summary>Text stavoveho stitku.</summary>
    public string HealthText { get => _healthText; private set => SetProperty(ref _healthText, value); }

    /// <summary>Barva stavu.</summary>
    public Brush HealthBrush { get => _healthBrush; private set => SetProperty(ref _healthBrush, value); }

    /// <summary>Informace o poslednim mereni.</summary>
    public string FooterText { get => _footerText; private set => SetProperty(ref _footerText, value); }

    /// <summary>Pruhlednost progressbaru (neaktualni data jsou potlacena).</summary>
    public double GaugeOpacity { get => _gaugeOpacity; private set => SetProperty(ref _gaugeOpacity, value); }

    /// <summary>Upozorneni nad progressbary, pokud jde o posledni znamou hodnotu.</summary>
    public string StaleText { get => _staleText; private set => SetProperty(ref _staleText, value); }

    /// <summary>Seznam problemu.</summary>
    public IReadOnlyList<IssueViewModel> Issues { get => _issues; private set => SetProperty(ref _issues, value); }

    /// <summary>Barva stanice (prouzek karty, nazev, prepinac).</summary>
    public Brush AccentBrush { get => _accentBrush; private set => SetProperty(ref _accentBrush, value); }

    /// <summary>Svetla varianta barvy stanice (zahlavi karty).</summary>
    public Brush AccentTint { get => _accentTint; private set => SetProperty(ref _accentTint, value); }

    /// <summary>Svetly podklad zapnuteho prepinace stanice.</summary>
    public Brush AccentChip { get => _accentChip; private set => SetProperty(ref _accentChip, value); }

    /// <summary>Tmavsi odstin barvy stanice pro text (citelnost na svetlem podkladu).</summary>
    public Brush AccentText { get => _accentText; private set => SetProperty(ref _accentText, value); }

    /// <summary>Karta je zobrazena (prepinac v horni liste).</summary>
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    /// <summary>
    /// Nastavi barvu stanice.
    /// </summary>
    public void SetAccent(Brush accent)
    {
        if (ReferenceEquals(accent, _accentBrush))
        {
            return;
        }

        AccentBrush = accent;
        AccentTint = Palette.Tint(accent, 0x18);
        AccentChip = Palette.Tint(accent, 0x33);
        AccentText = Palette.Darken(accent);
    }

    /// <summary>Posledni vyhodnoceni (pro alarmy).</summary>
    public StationEvaluation? Evaluation { get; private set; }

    /// <summary>
    /// Aktualizuje kartu z dat nactenych z DB.
    /// </summary>
    public void Update(StationSnapshot snapshot, StationEvaluation evaluation, DateTimeOffset now)
    {
        Station station = snapshot.Station;
        Measurement? measurement = snapshot.LatestMeasurement;
        Evaluation = evaluation;

        SortKey = station.StationCode;
        Title = string.IsNullOrWhiteSpace(station.Name) ? station.StationCode : station.Name;
        string location = !string.IsNullOrWhiteSpace(station.LocationText) ? station.LocationText : string.Empty;
        Subtitle = string.Join(" · ", new[] { station.StationCode, location, station.FirmwareVersion is { Length: > 0 } fw ? "FW " + fw : string.Empty }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        HealthBrush = Palette.ForHealth(evaluation.Health);
        HealthText = evaluation.Health switch
        {
            StationHealth.Ok => "V POŘÁDKU",
            StationHealth.Warning => "VAROVÁNÍ",
            StationHealth.Error => "CHYBA",
            _ => "NEMĚŘÍ"
        };

        foreach (PollutantGaugeViewModel gauge in Gauges)
        {
            gauge.Update(measurement, evaluation.SensorProblems);
        }

        double altitude = _pressureReduction.AltitudeFor(station.StationCode, station.LocationText);
        foreach (EnvironmentGaugeViewModel gauge in EnvironmentGauges)
        {
            gauge.Update(measurement, evaluation.SensorProblems, altitude, _pressureReduction);
        }

        GaugeOpacity = evaluation.IsStale ? 0.45 : 1.0;
        StaleText = evaluation.IsStale && measurement is not null ? "Poslední známé hodnoty – nejsou aktuální." : string.Empty;
        FooterText = BuildFooter(measurement, now);
        Issues = evaluation.Issues
            .Select(issue => new IssueViewModel(
                issue.Severity switch
                {
                    StationHealth.Offline => "⛔",
                    StationHealth.Error => "✖",
                    _ => "⚠"
                },
                issue.Message,
                Palette.ForHealth(issue.Severity)))
            .ToList();
    }

    private static string BuildFooter(Measurement? measurement, DateTimeOffset now)
    {
        if (measurement is null)
        {
            return "Žádné měření v databázi.";
        }

        List<string> parts =
        [
            $"Měřeno {StationHealthEvaluator.FormatTime(measurement.MeasuredAt, now)} (před {StationHealthEvaluator.FormatAge(now - measurement.MeasuredAt)})"
        ];

        string power = (measurement.PowerSource ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "BAT" => "baterie",
            "SRC" => "síť",
            var other => other
        };
        if (power.Length > 0)
        {
            parts.Add(measurement.BatteryVoltage is { } voltage
                ? $"napájení: {power} ({voltage.ToString("0.00", Czech)} V)"
                : $"napájení: {power}");
        }

        if (measurement.ExpectedSampleCount > 0)
        {
            parts.Add($"SPS30 {measurement.Sps30OkCount}/{measurement.ExpectedSampleCount} vzorků");
        }

        return string.Join(" · ", parts);
    }
}
