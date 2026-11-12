using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using MeteoStanice.Monitor.Models;
using MeteoStanice.Monitor.Services;

namespace MeteoStanice.Monitor.ViewModels;

/// <summary>
/// Hlavni view model: periodicky cte DB, aktualizuje karty stanic a ridi alarm.
/// </summary>
public sealed class MainViewModel(MonitorOptions options, StationDataReader reader, AlarmService alarms) : ObservableObject
{
    private const string DatabaseAlarmKey = "db";
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private bool _isRefreshing;
    private bool _refreshPending;
    private IReadOnlyList<StationSnapshot> _lastSnapshots = [];
    private int _visibleColumns = 3;
    private bool _isWidgetMode;
    private readonly HashSet<string> _hiddenStationCodes = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasNoVisibleStations;
    private readonly List<Brush> _stationColors = options.StationColors
        .Select(hex => Palette.FromHex(hex, Palette.Normal))
        .ToList();
    private string _updateModeText = string.Empty;
    private string _connectionText = "Připojuji k databázi…";
    private Brush _connectionBrush = Palette.Unknown;
    private string _lastRefreshText = string.Empty;
    private bool _isAlarmActive;
    private string _alarmText = string.Empty;
    private bool _hasNoStations;
    private bool _databaseFailed;
    private string _databaseError = string.Empty;

    /// <summary>Karty stanic.</summary>
    public ObservableCollection<StationViewModel> Stations { get; } = [];

    /// <summary>Stav pripojeni k DB.</summary>
    public string ConnectionText { get => _connectionText; private set => SetProperty(ref _connectionText, value); }

    /// <summary>Barva stavu pripojeni.</summary>
    public Brush ConnectionBrush { get => _connectionBrush; private set => SetProperty(ref _connectionBrush, value); }

    /// <summary>Cas posledniho obnoveni.</summary>
    public string LastRefreshText { get => _lastRefreshText; private set => SetProperty(ref _lastRefreshText, value); }

    /// <summary>Nazev aplikace (zahlavi, titulek okna).</summary>
    public string AppTitle => options.AppTitle;

    /// <summary>Zpusob obnovovani dat (LISTEN/NOTIFY nebo kontrola zmen).</summary>
    public string UpdateModeText { get => _updateModeText; set => SetProperty(ref _updateModeText, value); }

    /// <summary>Je aktivni nepotvrzeny alarm.</summary>
    public bool IsAlarmActive { get => _isAlarmActive; private set => SetProperty(ref _isAlarmActive, value); }

    /// <summary>Text alarmoveho pruhu.</summary>
    public string AlarmText { get => _alarmText; private set => SetProperty(ref _alarmText, value); }

    /// <summary>V DB neni zadna aktivni stanice.</summary>
    public bool HasNoStations { get => _hasNoStations; private set => SetProperty(ref _hasNoStations, value); }

    /// <summary>Okno je pripnute na plose jako widget (skryty nadpis, bez ramecku).</summary>
    public bool IsWidgetMode { get => _isWidgetMode; set => SetProperty(ref _isWidgetMode, value); }

    /// <summary>
    /// Zmenila se viditelnost nektere stanice (pro ulozeni stavu okna).
    /// </summary>
    public event Action? StationVisibilityChanged;

    /// <summary>
    /// Nastavi stanice skryte z minuleho spusteni (podle kodu stanice).
    /// </summary>
    public void SetHiddenStationCodes(IEnumerable<string> codes)
    {
        _hiddenStationCodes.Clear();
        _hiddenStationCodes.UnionWith(codes);
    }

    /// <summary>
    /// Kody aktualne skrytych stanic.
    /// </summary>
    public List<string> GetHiddenStationCodes()
        => Stations.Where(station => !station.IsVisible).Select(station => station.SortKey).ToList();

    /// <summary>Pocet sloupcu mrizky (= pocet zobrazenych stanic, max. 3).</summary>
    public int VisibleColumns { get => _visibleColumns; private set => SetProperty(ref _visibleColumns, value); }

    /// <summary>Uzivatel skryl vsechny stanice.</summary>
    public bool HasNoVisibleStations { get => _hasNoVisibleStations; private set => SetProperty(ref _hasNoVisibleStations, value); }

    private static string StationsWord(int count) => count switch
    {
        1 => "stanice",
        >= 2 and <= 4 => "stanice",
        _ => "stanic"
    };

    /// <summary>
    /// Zobrazi vsechny stanice.
    /// </summary>
    public void ShowAllStations()
    {
        foreach (StationViewModel station in Stations)
        {
            station.IsVisible = true;
        }
    }

    /// <summary>
    /// Zobrazi jen jednu stanici.
    /// </summary>
    public void ShowOnly(StationViewModel selected)
    {
        foreach (StationViewModel station in Stations)
        {
            station.IsVisible = ReferenceEquals(station, selected);
        }
    }

    private void OnStationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StationViewModel.IsVisible))
        {
            UpdateVisibleColumns();
            StationVisibilityChanged?.Invoke();
        }
    }

    private void UpdateVisibleColumns()
    {
        int visible = Stations.Count(station => station.IsVisible);
        HasNoVisibleStations = Stations.Count > 0 && visible == 0;
        VisibleColumns = Math.Clamp(visible, 1, 3);
    }

    /// <summary>Zvuk zapnut.</summary>
    public bool SoundEnabled
    {
        get => alarms.Enabled;
        set
        {
            if (alarms.Enabled != value)
            {
                alarms.Enabled = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Nacte data z DB a aktualizuje UI. Volat z UI vlakna.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            // Zmena prisla behem cteni – po dokonceni se nacte znovu.
            _refreshPending = true;
            return;
        }

        _isRefreshing = true;
        _refreshPending = false;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            IReadOnlyList<StationSnapshot> snapshots = await reader.ReadAsync(TimeSpan.FromHours(options.EventLookbackHours), timeout.Token);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            _lastSnapshots = snapshots;
            ApplySnapshots(snapshots, now);

            _databaseFailed = false;
            _databaseError = string.Empty;
            ConnectionText = $"Databáze připojena · {snapshots.Count} {StationsWord(snapshots.Count)}";
            ConnectionBrush = Palette.Good;
            LastRefreshText = $"data načtena {DateTime.Now.ToString("HH:mm:ss", Czech)}";
        }
        catch (Exception ex)
        {
            _databaseFailed = true;
            _databaseError = ex.GetBaseException().Message;
            ConnectionText = "Databáze nedostupná – zobrazená data nejsou aktuální";
            ConnectionBrush = Palette.High;
            LastRefreshText = $"pokus {DateTime.Now.ToString("HH:mm:ss", Czech)}";
        }
        finally
        {
            _isRefreshing = false;
        }

        UpdateAlarms();

        if (_refreshPending)
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Znovu vyhodnoti posledni nactena data vuci aktualnimu casu – bez dotazu do DB.
    /// Vypadek stanice se projevi plynutim casu, ne zmenou v databazi.
    /// </summary>
    public void Reevaluate()
    {
        if (_isRefreshing || _lastSnapshots.Count == 0)
        {
            return;
        }

        ApplySnapshots(_lastSnapshots, DateTimeOffset.UtcNow);
        UpdateAlarms();
    }

    /// <summary>
    /// Potvrdi aktualni alarmy (ztlumi zvuk, dokud nevznikne novy).
    /// </summary>
    public void AcknowledgeAlarms()
    {
        alarms.Acknowledge();
        UpdateAlarmBanner();
    }

    /// <summary>
    /// Periodicke opakovani zvuku nepotvrzeneho alarmu.
    /// </summary>
    public void SoundTick() => alarms.Tick();

    /// <summary>
    /// Testovaci prehrani zvuku.
    /// </summary>
    public void TestSound() => alarms.Play();

    private void ApplySnapshots(IReadOnlyList<StationSnapshot> snapshots, DateTimeOffset now)
    {
        HashSet<Guid> seen = [];
        foreach (StationSnapshot snapshot in snapshots)
        {
            seen.Add(snapshot.Station.Id);
            StationViewModel? card = Stations.FirstOrDefault(item => item.StationId == snapshot.Station.Id);
            bool isNew = card is null;
            if (card is null)
            {
                card = new StationViewModel(snapshot.Station.Id, options.Pollutants, options.EnvironmentGauges, options.PressureReduction);
                Stations.Add(card);
            }

            card.Update(snapshot, StationHealthEvaluator.Evaluate(snapshot, options, now), now);

            if (isNew)
            {
                // Skryti z minuleho spusteni (podle kodu stanice, ktery je znamy az po Update).
                card.IsVisible = !_hiddenStationCodes.Contains(card.SortKey);
                card.PropertyChanged += OnStationPropertyChanged;
            }
        }

        for (int index = Stations.Count - 1; index >= 0; index--)
        {
            if (!seen.Contains(Stations[index].StationId))
            {
                Stations[index].PropertyChanged -= OnStationPropertyChanged;
                Stations.RemoveAt(index);
            }
        }

        // Razeni podle kodu stanice bez znovuvytvareni karet.
        List<StationViewModel> sorted = Stations.OrderBy(item => item.SortKey, StringComparer.OrdinalIgnoreCase).ToList();
        for (int target = 0; target < sorted.Count; target++)
        {
            int current = Stations.IndexOf(sorted[target]);
            if (current != target)
            {
                Stations.Move(current, target);
            }
        }

        // Barva stanice podle poradi (kod stanice) – stabilni mezi obnovenimi.
        for (int index = 0; index < Stations.Count && _stationColors.Count > 0; index++)
        {
            Stations[index].SetAccent(_stationColors[index % _stationColors.Count]);
        }

        HasNoStations = Stations.Count == 0;
        UpdateVisibleColumns();
    }

    private void UpdateAlarms()
    {
        List<string> keys = [];
        if (_databaseFailed)
        {
            keys.Add(DatabaseAlarmKey);
        }

        foreach (StationViewModel station in Stations)
        {
            if (station.Evaluation is null)
            {
                continue;
            }

            foreach (StationIssue issue in station.Evaluation.Issues)
            {
                if (issue.Severity >= StationHealth.Error || options.Sound.AlarmOnWarnings)
                {
                    keys.Add($"{station.StationId}:{issue.Key}");
                }
            }

            if (options.Sound.AlarmOnHighPollution && !station.Evaluation.IsStale)
            {
                keys.AddRange(station.Gauges
                    .Where(gauge => gauge.Level == PollutionLevel.High)
                    .Select(gauge => $"{station.StationId}:High:{gauge.Code}"));
            }
        }

        bool hasNew = alarms.Update(keys);
        UpdateAlarmBanner();
        if (hasNew)
        {
            alarms.Tick(force: true);
        }
    }

    private void UpdateAlarmBanner()
    {
        IsAlarmActive = alarms.HasUnacknowledged;
        if (!IsAlarmActive)
        {
            AlarmText = string.Empty;
            return;
        }

        List<string> lines = [];
        if (_databaseFailed)
        {
            lines.Add($"Databáze nedostupná: {_databaseError}");
        }

        foreach (StationViewModel station in Stations)
        {
            if (station.Evaluation is null)
            {
                continue;
            }

            if (station.Evaluation.Health >= StationHealth.Error)
            {
                lines.Add($"{station.Title}: {station.HealthText.ToLower(Czech)} – {station.Evaluation.Issues[0].Message}");
            }

            if (options.Sound.AlarmOnHighPollution && !station.Evaluation.IsStale)
            {
                foreach (PollutantGaugeViewModel gauge in station.Gauges.Where(item => item.Level == PollutionLevel.High))
                {
                    lines.Add($"{station.Title}: vysoká koncentrace {gauge.Label} ({gauge.ValueText} {gauge.Unit})");
                }
            }
        }

        AlarmText = $"ALARM ({alarms.UnacknowledgedCount} nepotvrzených) – " + (lines.Count == 0 ? "viz karty stanic" : string.Join("   |   ", lines));
    }
}
