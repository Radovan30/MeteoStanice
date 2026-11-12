using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using MeteoStanice.Monitor.Services;
using MeteoStanice.Monitor.ViewModels;
using Microsoft.Extensions.Configuration;

namespace MeteoStanice.Monitor;

/// <summary>
/// Vstupni bod aplikace: nacte konfiguraci a sestavi hlavni okno.
/// </summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CultureInfo czech = CultureInfo.GetCultureInfo("cs-CZ");
        CultureInfo.DefaultThreadCurrentCulture = czech;
        CultureInfo.DefaultThreadCurrentUICulture = czech;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(czech.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.GetBaseException().Message, "MeteoStanice Monitor – neočekávaná chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // appsettings.Local.json (volitelny, neverzovat) a promenne prostredi prepisuji appsettings.json.
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        MonitorOptions options = configuration.GetSection("Monitor").Get<MonitorOptions>() ?? new MonitorOptions();
        options.ConnectionString = configuration.GetConnectionString("MeteoDatabase") ?? string.Empty;
        options.ApplyDefaults();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            MessageBox.Show(
                "V appsettings.json chybí ConnectionStrings:MeteoDatabase.",
                "MeteoStanice Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainViewModel viewModel = new(options, new StationDataReader(options.ConnectionString), new AlarmService(options.Sound));
        DatabaseChangeWatcher changeWatcher = new(options.ConnectionString, options.NotifyChannel);
        var window = new MeteoStanice.Monitor.MainWindow(viewModel, options, changeWatcher);
        MainWindow = window;
        window.Show();
    }
}
