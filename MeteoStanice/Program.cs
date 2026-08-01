using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Application entry point; only composes dependencies and starts the coordinator.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Creates hardware configuration, wires services and starts the station.
        /// </summary>
        public static void Main()
        {
            // Configuration is loaded first so the station can recover after restart.
            IStationConfigurationStore configurationStore = new FlashStationConfigurationStore();
            StationConfigurationService configurationService = new StationConfigurationService(configurationStore);
            StationConfiguration configuration = configurationService.LoadOrCreateDefault();

            // After boot the station opens a short setup window for local changes.
            WifiProvisioningService provisioningService = new WifiProvisioningService(configurationStore, configurationService);
            configuration = provisioningService.RunStartupWindow(configuration);

            // Time provider is injected so firmware logic can be tested deterministically.
            ITimeProvider timeProvider = new FakeTimeProvider();

            // Queues keep data locally while Wi-Fi or the server is unavailable.
            IPersistentQueue<MeasurementRecord> measurementQueue = new InMemoryPersistentQueue<MeasurementRecord>(configuration.LocalQueueCapacity);
            IPersistentQueue<DeviceEvent> eventQueue = new InMemoryPersistentQueue<DeviceEvent>(configuration.LocalQueueCapacity);

            // API and Wi-Fi scanner are behind interfaces for test and real hardware variants.
            IApiClient apiClient = new FakeApiClient();
            IWifiScanner wifiScanner = new FakeWifiScanner();
            RetryPolicy retryPolicy = new RetryPolicy(3, 250);

            // Station identity lets the server distinguish and manage multiple stations.
            StationIdentityService identityService = new StationIdentityService();
            StationIdentity stationIdentity = identityService.CreateIdentity(configuration);

            // Remote configuration can change Wi-Fi, IP, intervals and other station settings.
            RemoteConfigurationService remoteConfigurationService = new RemoteConfigurationService(apiClient, configurationStore, configurationService);
            configuration = remoteConfigurationService.CheckAndApply(configuration, stationIdentity);

            // Location is estimated from nearby Wi-Fi access points through the server.
            LocationService locationService = new LocationService(wifiScanner, apiClient, configurationStore, timeProvider);
            locationService.TryUpdateLocation(configuration, stationIdentity);

            // Sensors are injected as abstractions; fake implementations are used for now.
            ISensor<Sht40Sample> sht40 = new FakeSht40Sensor(timeProvider);
            ISensor<Bmp280Sample> bmp280 = new FakeBmp280Sensor(timeProvider);
            ISensor<Sps30Sample> sps30 = new FakeSps30Sensor(timeProvider);
            IPowerMonitor powerMonitor = new FakePowerMonitor(timeProvider);

            // Application services are composed once at startup.
            EventReportingService eventReportingService = new EventReportingService(
                eventQueue,
                timeProvider,
                configuration);

            MeasurementAggregationService aggregationService = new MeasurementAggregationService(
                configuration.ExpectedSamplesPerInterval);

            MeasurementUploadService uploadService = new MeasurementUploadService(
                measurementQueue,
                eventQueue,
                apiClient,
                retryPolicy);

            MeasurementCoordinator coordinator = new MeasurementCoordinator(
                configuration,
                timeProvider,
                sht40,
                bmp280,
                sps30,
                powerMonitor,
                aggregationService,
                measurementQueue,
                eventReportingService,
                uploadService);

            // The coordinator owns the infinite measurement loop.
            coordinator.Run();

            Thread.Sleep(Timeout.Infinite);
        }
    }
}
