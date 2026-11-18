using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MeteoStanice.Monitor.Controls;
using MeteoStanice.Monitor.Services;

namespace MeteoStanice.Monitor;

/// <summary>
/// Stanice nabizena v okne prubehu.
/// </summary>
/// <param name="Id">Stations.Id.</param>
/// <param name="Number">Kratke oznaceni na prepinaci (1, 2, 3).</param>
/// <param name="Title">Nazev stanice (legenda, hodnoty pod mysi).</param>
/// <param name="Brush">Barva stanice.</param>
public sealed record HistoryStation(Guid Id, string Number, string Title, Brush Brush);

/// <summary>
/// Okno s prubehem mereni: vyber stanic 1/2/3, rozsah 1–24 h a grafy PM1, PM2,5, PM4, PM10, teploty,
/// vlhkosti a tlaku (more) s jednou krivkou za kazdou vybranou stanici. Data jsou 10s vzorky
/// ze SensorSamples zprumerovane do oken (1 h = surove vzorky). Obnovuje se po notifikaci
/// o novych vzorcich (RequestRefresh), bez periodickeho dotazovani.
/// </summary>
public sealed class HistoryWindow : Window
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly (string Label, TimeSpan Range)[] Ranges =
    [
        ("1 h", TimeSpan.FromHours(1)),
        ("2 h", TimeSpan.FromHours(2)),
        ("8 h", TimeSpan.FromHours(8)),
        ("12 h", TimeSpan.FromHours(12)),
        ("24 h", TimeSpan.FromHours(24))
    ];

    private readonly HistoryReader _reader;
    private readonly MonitorOptions _options;
    private readonly IReadOnlyList<HistoryStation> _stations;
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, ToggleButton> _stationButtons = [];
    private readonly List<Button> _rangeButtons = [];
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TimeSeriesChart _pm1 = new() { Title = "PM1", Unit = "µg/m³", Decimals = 1, MinimumSpan = 5, Floor = 0 };
    private readonly TimeSeriesChart _pm25 = new() { Title = "PM2,5", Unit = "µg/m³", Decimals = 1, MinimumSpan = 5, Floor = 0 };
    private readonly TimeSeriesChart _pm4 = new() { Title = "PM4", Unit = "µg/m³", Decimals = 1, MinimumSpan = 5, Floor = 0 };
    private readonly TimeSeriesChart _pm10 = new() { Title = "PM10", Unit = "µg/m³", Decimals = 1, MinimumSpan = 5, Floor = 0 };
    private readonly TimeSeriesChart _temperature = new() { Title = "Teplota", Unit = "°C", Decimals = 1, MinimumSpan = 2 };
    private readonly TimeSeriesChart _humidity = new() { Title = "Relativní vlhkost", Unit = "%", Decimals = 0, MinimumSpan = 5 };
    private readonly TimeSeriesChart _pressure = new() { Title = "Tlak přepočtený na hladinu moře", Unit = "hPa", Decimals = 1, MinimumSpan = 2 };
    private readonly DispatcherTimer _throttleTimer = new();
    private TimeSpan _range = TimeSpan.FromHours(1);
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _refreshing;
    private bool _pending;

    public HistoryWindow(HistoryReader reader, MonitorOptions options, IReadOnlyList<HistoryStation> stations, Guid? onlyStation)
    {
        _reader = reader;
        _options = options;
        _stations = stations;
        foreach (HistoryStation station in stations.Where(item => onlyStation is null || item.Id == onlyStation))
        {
            _selected.Add(station.Id);
        }

        Title = "Průběh měření – " + (onlyStation is null ? "všechny stanice" : stations.FirstOrDefault(item => item.Id == onlyStation)?.Title ?? "stanice");
        Width = 980;
        Height = 860;
        MinWidth = 600;
        MinHeight = 500;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();

        _throttleTimer.Tick += async (_, _) =>
        {
            _throttleTimer.Stop();
            await RefreshAsync();
        };

        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _throttleTimer.Stop();
    }

    /// <summary>
    /// Vybere jen jednu stanici (napr. po kliknuti na graf v karte stanice) a obnovi data.
    /// </summary>
    public async void ShowOnly(Guid stationId)
    {
        _selected.Clear();
        _selected.Add(stationId);
        UpdateButtons();
        await RefreshAsync();
    }

    /// <summary>
    /// Prisly nove vzorky. Obnovi se nejvyse jednou za delku prumerovaciho okna (min. 10 s).
    /// </summary>
    public void RequestRefresh()
    {
        TimeSpan minimum = TimeSpan.FromSeconds(HistoryReader.BucketSecondsFor(_range));
        TimeSpan since = DateTime.UtcNow - _lastRefresh;
        if (since >= minimum)
        {
            _ = RefreshAsync();
            return;
        }

        if (!_throttleTimer.IsEnabled)
        {
            _throttleTimer.Interval = minimum - since;
            _throttleTimer.Start();
        }
    }

    private UIElement BuildLayout()
    {
        StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 6) };
        toolbar.Children.Add(Label("Stanice:"));
        foreach (HistoryStation station in _stations)
        {
            ToggleButton button = new()
            {
                Content = station.Number,
                ToolTip = station.Title,
                MinWidth = 34,
                Height = 28,
                Margin = new Thickness(0, 0, 4, 0),
                FontWeight = FontWeights.SemiBold,
                BorderBrush = station.Brush,
                BorderThickness = new Thickness(2)
            };
            button.Click += async (_, _) =>
            {
                if (!_selected.Add(station.Id))
                {
                    _selected.Remove(station.Id);
                }

                UpdateButtons();
                await RefreshAsync();
            };
            _stationButtons[station.Id] = button;
            toolbar.Children.Add(button);
        }

        toolbar.Children.Add(Label("Období:", 16));
        foreach ((string label, TimeSpan range) in Ranges)
        {
            Button button = new() { Content = label, Tag = range, MinWidth = 44, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
            button.Click += async (_, _) =>
            {
                _range = range;
                UpdateButtons();
                await RefreshAsync();
            };
            _rangeButtons.Add(button);
            toolbar.Children.Add(button);
        }

        toolbar.Children.Add(_status);
        UpdateButtons();

        StackPanel charts = new() { Margin = new Thickness(12, 0, 12, 12) };
        foreach (TimeSeriesChart chart in new[] { _pm1, _pm25, _pm4, _pm10, _temperature, _humidity, _pressure })
        {
            chart.Height = 150;
            chart.Margin = new Thickness(0, 6, 0, 0);
            charts.Children.Add(chart);
        }

        TextBlock note = new()
        {
            Text = "Hodnoty: 10s vzorky ze SensorSamples zprůměrované do oken (1 h = surové vzorky, 24 h = 4 min). PM včetně korekce SPS30 stanice, tlak přepočtený na hladinu moře jako na ukazateli. Najetím myší se zobrazí hodnoty v daném čase.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)),
            Margin = new Thickness(0, 8, 0, 0)
        };
        charts.Children.Add(note);

        DockPanel root = new();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(new ScrollViewer { Content = charts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }

    private void UpdateButtons()
    {
        foreach (HistoryStation station in _stations)
        {
            if (_stationButtons.TryGetValue(station.Id, out ToggleButton? button))
            {
                bool selected = _selected.Contains(station.Id);
                button.IsChecked = selected;
                button.Background = selected ? station.Brush : Brushes.White;
                button.Foreground = selected ? Brushes.White : station.Brush;
            }
        }

        foreach (Button button in _rangeButtons)
        {
            bool selected = button.Tag is TimeSpan range && range == _range;
            button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
            button.Background = selected ? new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xF3)) : Brushes.White;
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _pending = true;
            return;
        }

        _refreshing = true;
        _pending = false;
        _lastRefresh = DateTime.UtcNow;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            List<HistoryStation> selected = _stations.Where(item => _selected.Contains(item.Id)).ToList();
            HistoryResult result = await _reader.ReadAsync(selected.Select(item => item.Id).ToList(), _range, _options.PressureReduction, timeout.Token);

            // Krivka se prerusi pri vypadku delsim nez 3 prumerovaci okna (min. 1 min).
            TimeSpan maxGap = TimeSpan.FromSeconds(Math.Max(60, HistoryReader.BucketSecondsFor(_range) * 3));
            Set(_pm1, selected, result, point => point.Pm1, maxGap);
            Set(_pm25, selected, result, point => point.Pm25, maxGap);
            Set(_pm4, selected, result, point => point.Pm4, maxGap);
            Set(_pm10, selected, result, point => point.Pm10, maxGap);
            Set(_temperature, selected, result, point => point.Temperature, maxGap);
            Set(_humidity, selected, result, point => point.Humidity, maxGap);
            Set(_pressure, selected, result, point => point.PressureSeaLevel, maxGap);

            int count = result.Series.Values.Sum(points => points.Count);
            _status.Text = selected.Count == 0
                ? "vyberte stanici"
                : $"načteno {DateTime.Now.ToString("HH:mm:ss", Czech)} · {count} bodů · okno {result.BucketSeconds} s";
        }
        catch (Exception ex)
        {
            _status.Text = "data nelze načíst: " + ex.GetBaseException().Message;
        }
        finally
        {
            _refreshing = false;
        }

        if (_pending)
        {
            await RefreshAsync();
        }
    }

    private static void Set(TimeSeriesChart chart, IReadOnlyList<HistoryStation> stations, HistoryResult result, Func<HistoryPoint, double?> selector, TimeSpan maxGap)
    {
        List<ChartSeries> series = [];
        foreach (HistoryStation station in stations)
        {
            if (!result.Series.TryGetValue(station.Id, out List<HistoryPoint>? points))
            {
                continue;
            }

            List<ChartPoint> chartPoints = points
                .Where(point => selector(point) is not null)
                .Select(point => new ChartPoint(point.Time, selector(point)!.Value))
                .ToList();
            series.Add(new ChartSeries(station.Title, station.Brush, chartPoints));
        }

        chart.MaxGap = maxGap;
        chart.SetData(series, result.From, result.To);
    }

    private static TextBlock Label(string text, double leftMargin = 0)
        => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(leftMargin, 0, 6, 0), FontWeight = FontWeights.SemiBold };
}
