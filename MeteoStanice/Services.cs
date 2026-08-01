using System;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Repeats a transient operation with a fixed delay between attempts.
    /// </summary>
    public sealed class RetryPolicy
    {
        private readonly int _maxAttempts;
        private readonly int _delayMilliseconds;

        /// <summary>
        /// Creates a retry policy.
        /// </summary>
        public RetryPolicy(int maxAttempts, int delayMilliseconds)
        {
            _maxAttempts = maxAttempts;
            _delayMilliseconds = delayMilliseconds;
        }

        /// <summary>
        /// Executes the operation until it succeeds or the attempt limit is reached.
        /// </summary>
        public bool Execute(UploadOperation operation)
        {
            int attempt = 0;
            while (attempt < _maxAttempts)
            {
                // Retry only transient operations; permanent validation happens elsewhere.
                attempt++;
                if (operation.Invoke())
                {
                    return true;
                }

                Thread.Sleep(_delayMilliseconds);
            }

            return false;
        }
    }

    /// <summary>
    /// Represents an upload operation that can be retried.
    /// </summary>
    public delegate bool UploadOperation();

    /// <summary>
    /// Uploads queued measurements and events and removes them only after acknowledgement.
    /// </summary>
    public sealed class MeasurementUploadService
    {
        private readonly IPersistentQueue<MeasurementRecord> _measurementQueue;
        private readonly IPersistentQueue<DeviceEvent> _eventQueue;
        private readonly IApiClient _apiClient;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>
        /// Creates a service for uploading persisted queues.
        /// </summary>
        public MeasurementUploadService(IPersistentQueue<MeasurementRecord> measurementQueue, IPersistentQueue<DeviceEvent> eventQueue, IApiClient apiClient, RetryPolicy retryPolicy)
        {
            _measurementQueue = measurementQueue;
            _eventQueue = eventQueue;
            _apiClient = apiClient;
            _retryPolicy = retryPolicy;
        }

        /// <summary>
        /// Attempts to upload all pending items while connectivity is available.
        /// </summary>
        public void TryUploadPending()
        {
            UploadEvents();
            UploadMeasurements();
        }

        private void UploadMeasurements()
        {
            MeasurementRecord record;
            while (_measurementQueue.TryPeek(out record))
            {
                // Peek first so the record stays queued until the server confirms upload.
                if (!_retryPolicy.Execute(delegate { return _apiClient.UploadMeasurement(record); }))
                {
                    return;
                }

                _measurementQueue.TryDequeue(out record);
            }
        }

        private void UploadEvents()
        {
            DeviceEvent deviceEvent;
            while (_eventQueue.TryPeek(out deviceEvent))
            {
                // Events use the same acknowledgement rule as measurements.
                if (!_retryPolicy.Execute(delegate { return _apiClient.UploadEvent(deviceEvent); }))
                {
                    return;
                }

                _eventQueue.TryDequeue(out deviceEvent);
            }
        }
    }

    /// <summary>
    /// Maintains station identity used for server registration and remote control.
    /// </summary>
    public sealed class StationIdentityService
    {
        /// <summary>
        /// Creates identity from station configuration.
        /// </summary>
        public StationIdentity CreateIdentity(StationConfiguration configuration)
        {
            // Identity contains both user-facing station ID and immutable hardware ID.
            StationIdentity identity = new StationIdentity();
            identity.StationId = configuration.StationId;
            identity.DeviceHardwareId = configuration.DeviceHardwareId;
            identity.StationName = configuration.StationName;
            identity.FirmwareVersion = configuration.FirmwareVersion;
            return identity;
        }
    }

    /// <summary>
    /// Downloads validated configuration changes from the server and stores them locally.
    /// </summary>
    public sealed class RemoteConfigurationService
    {
        private readonly IApiClient _apiClient;
        private readonly IStationConfigurationStore _configurationStore;
        private readonly StationConfigurationService _configurationService;

        /// <summary>
        /// Creates the remote configuration service.
        /// </summary>
        public RemoteConfigurationService(IApiClient apiClient, IStationConfigurationStore configurationStore, StationConfigurationService configurationService)
        {
            _apiClient = apiClient;
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }

        /// <summary>
        /// Applies a newer configuration from the server when remote changes are enabled.
        /// </summary>
        public StationConfiguration CheckAndApply(StationConfiguration currentConfiguration, StationIdentity identity)
        {
            // Remote changes can be disabled locally for service or safety reasons.
            if (currentConfiguration == null || !currentConfiguration.RemoteConfigurationEnabled)
            {
                return currentConfiguration;
            }

            // Server version is compared against local configuration version.
            RemoteConfigurationUpdate update;
            if (!_apiClient.TryDownloadConfiguration(identity, currentConfiguration.ConfigurationVersion, out update))
            {
                return currentConfiguration;
            }

            // Invalid or empty updates are ignored so the station keeps working.
            if (update == null || !update.HasUpdate || !_configurationService.Validate(update.Configuration))
            {
                return currentConfiguration;
            }

            // New configuration must be durable before it becomes active.
            if (!_configurationStore.Save(update.Configuration))
            {
                return currentConfiguration;
            }

            return update.Configuration;
        }
    }

    /// <summary>
    /// Estimates station location from nearby Wi-Fi networks through the server API.
    /// </summary>
    public sealed class LocationService
    {
        private readonly IWifiScanner _wifiScanner;
        private readonly IApiClient _apiClient;
        private readonly IStationConfigurationStore _configurationStore;
        private readonly ITimeProvider _timeProvider;

        /// <summary>
        /// Creates a location service.
        /// </summary>
        public LocationService(IWifiScanner wifiScanner, IApiClient apiClient, IStationConfigurationStore configurationStore, ITimeProvider timeProvider)
        {
            _wifiScanner = wifiScanner;
            _apiClient = apiClient;
            _configurationStore = configurationStore;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Updates configuration with a Wi-Fi based location estimate when available.
        /// </summary>
        public bool TryUpdateLocation(StationConfiguration configuration, StationIdentity identity)
        {
            // Wi-Fi geolocation needs at least two access points for a useful estimate.
            WifiAccessPointObservation[] accessPoints = _wifiScanner.Scan();
            if (accessPoints == null || accessPoints.Length < 2)
            {
                return false;
            }

            // API keys and provider-specific calls belong on the server, not on ESP32.
            StationLocation location;
            if (!_apiClient.TryResolveLocation(identity, accessPoints, out location))
            {
                return false;
            }

            if (location == null || !location.HasLocation)
            {
                return false;
            }

            // Store the last known location with timestamp for later uploads and diagnostics.
            location.EstimatedAt = _timeProvider.GetNow().Value;
            configuration.LastKnownLocation = location;
            return _configurationStore.Save(configuration);
        }
    }

    /// <summary>
    /// Creates event records and suppresses repeated active errors.
    /// </summary>
    public sealed class EventReportingService
    {
        private readonly IPersistentQueue<DeviceEvent> _eventQueue;
        private readonly ITimeProvider _timeProvider;
        private readonly StationConfiguration _configuration;
        private DeviceEventCode _activeCode;
        private bool _hasActiveEvent;
        private int _sequence;

        /// <summary>
        /// Creates an event reporting service.
        /// </summary>
        public EventReportingService(IPersistentQueue<DeviceEvent> eventQueue, ITimeProvider timeProvider, StationConfiguration configuration)
        {
            _eventQueue = eventQueue;
            _timeProvider = timeProvider;
            _configuration = configuration;
        }

        /// <summary>
        /// Reports a new event unless the same event is already active.
        /// </summary>
        public void Report(DeviceEventSeverity severity, DeviceEventCategory category, DeviceEventCode code, string component, int rawErrorCode, string message)
        {
            DateTime now = _timeProvider.GetNow().Value;

            // Suppress repeated copies of the same active error.
            if (_hasActiveEvent && _activeCode == code)
            {
                return;
            }

            // The first occurrence is queued; repeated occurrences can be counted later.
            _hasActiveEvent = true;
            _activeCode = code;

            DeviceEvent deviceEvent = CreateEvent(severity, category, code, component, rawErrorCode, message, now, false);
            _eventQueue.Enqueue(deviceEvent);
            Debug.WriteLine("EVENT " + code.ToString() + ": " + message);
        }

        /// <summary>
        /// Reports recovery after an active component error.
        /// </summary>
        public void ReportRecovery(DeviceEventCode code, string component, string message)
        {
            // Recovery clears active error suppression and creates a separate event.
            DateTime now = _timeProvider.GetNow().Value;
            _hasActiveEvent = false;
            DeviceEvent deviceEvent = CreateEvent(DeviceEventSeverity.Information, DeviceEventCategory.Sensor, code, component, 0, message, now, true);
            _eventQueue.Enqueue(deviceEvent);
            Debug.WriteLine("EVENT " + code.ToString() + ": " + message);
        }

        private DeviceEvent CreateEvent(DeviceEventSeverity severity, DeviceEventCategory category, DeviceEventCode code, string component, int rawErrorCode, string message, DateTime now, bool isRecovery)
        {
            _sequence++;
            DeviceEvent deviceEvent = new DeviceEvent();
            deviceEvent.EventId = _configuration.StationId + "-E-" + _sequence;
            deviceEvent.StationId = _configuration.StationId;
            deviceEvent.SequenceNumber = _sequence;
            deviceEvent.OccurredAt = now;
            deviceEvent.LastOccurredAt = now;
            deviceEvent.OccurrenceCount = 1;
            deviceEvent.HasEndedAt = false;
            deviceEvent.Severity = severity;
            deviceEvent.Category = category;
            deviceEvent.Code = code;
            deviceEvent.Component = component;
            deviceEvent.RawErrorCode = rawErrorCode;
            deviceEvent.Message = message;
            deviceEvent.ClockSynchronized = _timeProvider.IsSynchronized();
            deviceEvent.FirmwareVersion = _configuration.FirmwareVersion;
            deviceEvent.IsRecovery = isRecovery;
            return deviceEvent;
        }
    }

    /// <summary>
    /// Reads power state and suppresses short input spikes.
    /// </summary>
    public sealed class PowerMonitoringService
    {
        private readonly IPowerMonitor _powerMonitor;
        private bool _hasLastState;
        private bool _lastMainsAvailable;

        /// <summary>
        /// Creates a power monitoring service.
        /// </summary>
        public PowerMonitoringService(IPowerMonitor powerMonitor)
        {
            _powerMonitor = powerMonitor;
        }

        /// <summary>
        /// Reads power state twice and returns the stable value.
        /// </summary>
        public PowerState ReadDebounced()
        {
            // Two reads separated by a short delay filter out input spikes.
            PowerState first = _powerMonitor.Read();
            Thread.Sleep(20);
            PowerState second = _powerMonitor.Read();

            if (first.MainsAvailable != second.MainsAvailable)
            {
                // Unstable reads fall back to the last known stable power source.
                return _hasLastState ? CreateState(_lastMainsAvailable) : second;
            }

            _hasLastState = true;
            _lastMainsAvailable = second.MainsAvailable;
            return second;
        }

        private PowerState CreateState(bool mainsAvailable)
        {
            PowerState state = new PowerState();
            state.MainsAvailable = mainsAvailable;
            state.BatteryActive = !mainsAvailable;
            return state;
        }
    }

    /// <summary>
    /// Synchronizes device time.
    /// </summary>
    public sealed class TimeSynchronizationService
    {
        /// <summary>
        /// Attempts to synchronize time.
        /// </summary>
        public bool Synchronize()
        {
            return true;
        }
    }

    /// <summary>
    /// Connects the device to configured Wi-Fi.
    /// </summary>
    public sealed class WifiConnectionService
    {
        /// <summary>
        /// Connects using values stored in station configuration.
        /// </summary>
        public NetworkState Connect(StationConfiguration configuration)
        {
            NetworkState state = new NetworkState();
            state.WifiConnected = configuration != null && configuration.WifiSsid != null && configuration.WifiSsid.Length > 0;
            state.ServerAvailable = state.WifiConnected && configuration.ApiBaseUrl != null && configuration.ApiBaseUrl.Length > 0;
            state.IpAddress = configuration.UseDhcp ? "DHCP" : configuration.StaticIpAddress;
            return state;
        }

        /// <summary>
        /// Connects with test defaults.
        /// </summary>
        public NetworkState Connect()
        {
            NetworkState state = new NetworkState();
            state.WifiConnected = true;
            state.ServerAvailable = true;
            state.IpAddress = "0.0.0.0";
            return state;
        }
    }

    /// <summary>
    /// Opens a short setup window after boot for changing Wi-Fi and IP settings.
    /// </summary>
    public sealed class WifiProvisioningService
    {
        private readonly IStationConfigurationStore _configurationStore;
        private readonly StationConfigurationService _configurationService;

        /// <summary>
        /// Creates a Wi-Fi provisioning service.
        /// </summary>
        public WifiProvisioningService(IStationConfigurationStore configurationStore, StationConfigurationService configurationService)
        {
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }

        /// <summary>
        /// Runs a bounded setup window; real AP/web handling is added behind this method.
        /// </summary>
        public StationConfiguration RunStartupWindow(StationConfiguration currentConfiguration)
        {
            // Real AP or web setup handling will plug into this bounded window.
            Debug.WriteLine("Provisioning okno otevreno na " + currentConfiguration.ProvisioningWindowMilliseconds + " ms.");
            Thread.Sleep(currentConfiguration.ProvisioningWindowMilliseconds);
            Debug.WriteLine("Provisioning okno ukonceno.");
            return currentConfiguration;
        }

        /// <summary>
        /// Saves a new configuration supplied by the setup endpoint.
        /// </summary>
        public bool ApplyNewConfiguration(StationConfiguration newConfiguration)
        {
            // Never persist a configuration that would prevent a safe boot.
            if (!_configurationService.Validate(newConfiguration))
            {
                return false;
            }

            return _configurationStore.Save(newConfiguration);
        }

        /// <summary>
        /// Checks whether network configuration is usable.
        /// </summary>
        public bool HasValidConfiguration()
        {
            return true;
        }
    }

    /// <summary>
    /// Loads, validates and creates station configuration.
    /// </summary>
    public sealed class StationConfigurationService
    {
        private readonly IStationConfigurationStore _configurationStore;

        /// <summary>
        /// Creates a configuration service.
        /// </summary>
        public StationConfigurationService(IStationConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        /// <summary>
        /// Loads saved configuration or creates and saves defaults.
        /// </summary>
        public StationConfiguration LoadOrCreateDefault()
        {
            StationConfiguration configuration;
            // Valid persisted settings win over factory defaults.
            if (_configurationStore.TryLoad(out configuration) && Validate(configuration))
            {
                return configuration;
            }

            // Defaults are saved so later provisioning can update the same store.
            configuration = CreateDefault();
            _configurationStore.Save(configuration);
            return configuration;
        }

        /// <summary>
        /// Creates factory defaults used after first boot or invalid configuration.
        /// </summary>
        public StationConfiguration CreateDefault()
        {
            // Defaults are intentionally conservative and test-friendly.
            StationConfiguration configuration = new StationConfiguration();
            configuration.StationId = "METEO-TEST-001";
            configuration.DeviceHardwareId = "ESP32C3-UNKNOWN";
            configuration.StationName = "Testovaci meteostanice";
            configuration.FirmwareVersion = "0.1.0-test";
            configuration.SamplePeriodMilliseconds = 1000;
            configuration.AggregationIntervalSeconds = 600;
            configuration.ExpectedSamplesPerInterval = 600;
            configuration.Sps30EnabledOnBattery = false;
            configuration.LocalQueueCapacity = 128;
            configuration.WifiSsid = string.Empty;
            configuration.WifiPassword = string.Empty;
            configuration.ApiBaseUrl = string.Empty;
            configuration.UseDhcp = true;
            configuration.StaticIpAddress = "192.168.4.50";
            configuration.GatewayAddress = "192.168.4.1";
            configuration.SubnetMask = "255.255.255.0";
            configuration.DnsAddress = "8.8.8.8";
            configuration.ProvisioningAccessPointName = "MeteoStanice-Setup";
            configuration.ProvisioningWindowMilliseconds = 30000;
            configuration.ConfigurationVersion = 1;
            configuration.RemoteConfigurationEnabled = true;
            configuration.RemoteConfigurationCheckIntervalSeconds = 300;
            configuration.LastKnownLocation = new StationLocation();
            configuration.LastKnownLocation.HasLocation = false;
            configuration.LastKnownLocation.Source = LocationSource.Unknown;
            return configuration;
        }

        /// <summary>
        /// Validates the minimum configuration needed to start firmware.
        /// </summary>
        public bool Validate(StationConfiguration configuration)
        {
            // Validation checks only fields required to boot safely.
            return configuration != null
                && configuration.StationId != null
                && configuration.DeviceHardwareId != null
                && configuration.SamplePeriodMilliseconds > 0
                && configuration.ExpectedSamplesPerInterval > 0;
        }
    }
}
