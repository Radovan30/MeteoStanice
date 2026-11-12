using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MeteoStanice.Monitor.Services;
using MeteoStanice.Monitor.ViewModels;

namespace MeteoStanice.Monitor;

/// <summary>
/// Hlavni okno. Data se z DB nacitaji jen pri notifikaci o zmene (LISTEN/NOTIFY).
/// Casovac pouze prepocitava stari jiz nactenych dat; zalozni nacteni bezi jen bez notifikaci.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DatabaseChangeWatcher _changeWatcher;
    private readonly DispatcherTimer _reevaluateTimer;
    private readonly DispatcherTimer _fallbackTimer;
    private readonly DispatcherTimer _soundTimer;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer? _safetyTimer;

    // Sirka karty (CardWidth v XAML) + okraje karty (2 x 6) a okraj mrizky (2 x 6).
    private const double CardSlotWidth = 360 + 12;
    // Okraj mrizky (2 x 6) + obrys korenoveho ramecku (2 x 1).
    private const double GridMargin = 12 + 2;
    // Okno nema systemovy ramecek; "chrome" = okraj korenoveho Borderu pro stin (2 x 10).
    private double _chromeWidth = 20;
    private bool _layoutReady;
    private readonly DesktopPinning _pinning;
    private readonly UiState _state;

    /// <summary>
    /// Vytvori okno.
    /// </summary>
    public MainWindow(MainViewModel viewModel, MonitorOptions options, DatabaseChangeWatcher changeWatcher)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _changeWatcher = changeWatcher;
        DataContext = viewModel;
        _pinning = new DesktopPinning(this);

        // Stav z minuleho spusteni: poloha, skryte stanice, rezim widgetu.
        _state = UiStateStore.Load();
        _viewModel.SetHiddenStationCodes(_state.HiddenStations);
        _viewModel.StationVisibilityChanged += SaveState;
        RestorePosition();

        // Vypadek stanice = plynuti casu bez noveho mereni; prepocet bez dotazu do DB.
        _reevaluateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(options.ReevaluateIntervalSeconds) };
        _reevaluateTimer.Tick += (_, _) => _viewModel.Reevaluate();

        // Zaloha: spousti se jen, kdyz notifikace nejsou k dispozici.
        _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(options.FallbackRefreshSeconds) };
        _fallbackTimer.Tick += async (_, _) => await _viewModel.RefreshAsync();

        // Pojistka: obcasne uplne nacteni i pri fungujicich notifikacich (kdyby se nejaka notifikace ztratila).
        if (options.SafetyRefreshMinutes > 0)
        {
            _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(options.SafetyRefreshMinutes) };
            _safetyTimer.Tick += async (_, _) => await _viewModel.RefreshAsync();
        }

        _soundTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _soundTimer.Tick += (_, _) => _viewModel.SoundTick();

        // Mereni + priznaky kvality + LastSeenAt prichazeji v nekolika transakcich tesne po sobe – sloucit do jednoho nacteni.
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await _viewModel.RefreshAsync();
        };

        _changeWatcher.Changed += () => Dispatcher.InvokeAsync(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });

        _changeWatcher.StatusChanged += (notifyActive, text) => Dispatcher.InvokeAsync(() =>
        {
            // Text jen pri problemu; pri fungujicich notifikacich je zbytecny.
            _viewModel.UpdateModeText = notifyActive ? string.Empty : "⚠ " + text;
            if (notifyActive)
            {
                _fallbackTimer.Stop();
            }
            else
            {
                _fallbackTimer.Start();
            }
        });

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += async (_, _) =>
        {
            // Okraj okna = rozdil mezi sirkou okna a korenoveho Borderu (misto pro stin).
            if (Content is FrameworkElement content && content.ActualWidth > 0)
            {
                _chromeWidth = ActualWidth - content.ActualWidth;
            }

            _layoutReady = true;
            AdjustWidthToStations();

            await _viewModel.RefreshAsync();
            _reevaluateTimer.Start();
            _fallbackTimer.Start(); // zastavi se, jakmile se potvrdi LISTEN/NOTIFY
            _soundTimer.Start();
            _safetyTimer?.Start();
            _changeWatcher.Start();

            // Pripnuti az po prvnim nacteni dat (okno ma spravnou velikost a stanice jsou zname).
            if (_state.WidgetMode)
            {
                SetWidgetMode(true);
            }
        };

        Closing += (_, _) => SaveState();

        Closed += async (_, _) =>
        {
            _reevaluateTimer.Stop();
            _fallbackTimer.Stop();
            _soundTimer.Stop();
            _debounceTimer.Stop();
            _safetyTimer?.Stop();
            await _changeWatcher.DisposeAsync();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.VisibleColumns))
        {
            AdjustWidthToStations();
        }
    }

    /// <summary>
    /// Sirka okna podle poctu zobrazenych stanic (1–3 karty vedle sebe), vyska podle obsahu.
    /// </summary>
    private void AdjustWidthToStations()
    {
        if (!_layoutReady || WindowState != WindowState.Normal)
        {
            return;
        }

        double width = _chromeWidth + GridMargin + _viewModel.VisibleColumns * CardSlotWidth;
        Width = Math.Max(MinWidth, width);
        SizeToContent = SizeToContent.Height;

        // Okno nesmi po zmene sirky presahovat pravy okraj obrazovky.
        double right = SystemParameters.WorkArea.Right;
        if (Left + Width > right)
        {
            Left = Math.Max(SystemParameters.WorkArea.Left, right - Width);
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e) => _viewModel.ShowAllStations();

    private void Pin_Click(object sender, RoutedEventArgs e) => SetWidgetMode(!_viewModel.IsWidgetMode);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>
    /// Rezim widgetu: okno s pruhlednym pozadim pripnute na plose (za ostatnimi okny, bez hlavniho panelu, prezije Win+D).
    /// </summary>
    private void SetWidgetMode(bool enabled)
    {
        // Vzhled (pruhledne pozadi, polopruhledna lista) prepinaji DataTriggery v XAML podle IsWidgetMode.
        _viewModel.IsWidgetMode = enabled;
        _pinning.SetPinned(enabled);

        // Sirka/vyska az po prepoctu rozvrzeni bez ramecku.
        Dispatcher.InvokeAsync(() =>
        {
            AdjustWidthToStations();
            SaveState();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Presun okna tazenim za horni listu (v rezimu widgetu nema okno titulek).
    /// </summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || e.ClickCount > 1)
        {
            return;
        }

        try
        {
            DragMove();
            SaveState();
        }
        catch (InvalidOperationException)
        {
            // DragMove lze volat jen pri stisknutem levem tlacitku
        }
    }

    private void RestorePosition()
    {
        if (_state.Left is not { } left || _state.Top is not { } top)
        {
            return;
        }

        // Jen pokud je ulozena poloha na nekterem z monitoru (napr. po odpojeni druheho monitoru neni).
        bool visible = left >= SystemParameters.VirtualScreenLeft - 50
            && top >= SystemParameters.VirtualScreenTop - 10
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;
        if (visible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    private void SaveState()
    {
        if (!_layoutReady)
        {
            return;
        }

        _state.WidgetMode = _viewModel.IsWidgetMode;
        if (WindowState == WindowState.Normal)
        {
            _state.Left = Left;
            _state.Top = Top;
        }

        // Pred prvnim nactenim stanic by se ulozeny vyber prepsal prazdnym seznamem.
        if (_viewModel.Stations.Count > 0)
        {
            _state.HiddenStations = _viewModel.GetHiddenStationCodes();
        }

        UiStateStore.Save(_state);
    }

    private void StationToggle_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ToggleButton { DataContext: StationViewModel station })
        {
            _viewModel.ShowOnly(station);
            e.Handled = true;
        }
    }

    private async void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        // Potvrzeni jen ztlumi alarm; zaroven se nactou cerstva data, aby karta hned odpovidala DB.
        _viewModel.AcknowledgeAlarms();
        await _viewModel.RefreshAsync();
    }

    private void TestSound_Click(object sender, RoutedEventArgs e) => _viewModel.TestSound();
}
