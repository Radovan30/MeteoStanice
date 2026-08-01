using System;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Provides deterministic time for firmware smoke tests.
    /// </summary>
    public sealed class FakeTimeProvider : ITimeProvider
    {
        private DateTime _now;

        /// <summary>
        /// Creates the fake time provider at a stable starting time.
        /// </summary>
        public FakeTimeProvider()
        {
            _now = new DateTime(2026, 1, 1, 10, 0, 0);
        }

        /// <summary>
        /// Returns time advanced by one second per call.
        /// </summary>
        public DateTimeState GetNow()
        {
            _now = _now.AddSeconds(1);
            DateTimeState state = new DateTimeState();
            state.Value = _now;
            return state;
        }

        /// <summary>
        /// Returns whether fake time is considered synchronized.
        /// </summary>
        public bool IsSynchronized()
        {
            return true;
        }
    }

    /// <summary>
    /// Test SHT40 sensor with switchable CRC and read failures.
    /// </summary>
    public sealed class FakeSht40Sensor : ISensor<Sht40Sample>
    {
        private readonly ITimeProvider _timeProvider;
        private SensorHealth _health;

        // Test switch for SHT40 CRC failure scenario.
        public bool ForceCrcFailure { get; set; }

        // Test switch for SHT40 timeout scenario.
        public bool ForceReadFailure { get; set; }

        /// <summary>
        /// Creates a fake SHT40 sensor.
        /// </summary>
        public FakeSht40Sensor(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _health = SensorHealth.Unknown;
        }

        /// <summary>
        /// Marks the fake sensor as initialized.
        /// </summary>
        public SensorInitializationResult Initialize()
        {
            _health = SensorHealth.Ready;
            return SensorInitializationResult.Success;
        }

        /// <summary>
        /// Returns one fake SHT40 sample or a configured failure.
        /// </summary>
        public SensorReadResult<Sht40Sample> Read()
        {
            if (ForceReadFailure)
            {
                _health = SensorHealth.Failed;
                return SensorReadResult<Sht40Sample>.Failure(SensorErrorCode.Timeout, -1, "SHT40 neodpovedel v limitu.");
            }

            DateTime now = _timeProvider.GetNow().Value;
            Sht40Sample sample = new Sht40Sample();
            sample.SampledAt = now;
            sample.TemperatureCelsius = 22.5;
            sample.RelativeHumidityPercent = 48.0;
            sample.CrcOk = !ForceCrcFailure;
            _health = SensorHealth.Ready;

            if (!sample.CrcOk)
            {
                _health = SensorHealth.Degraded;
                return SensorReadResult<Sht40Sample>.Failure(SensorErrorCode.CrcFailed, -2, "SHT40 vratil data s chybnym CRC.");
            }

            return SensorReadResult<Sht40Sample>.Success(sample);
        }

        /// <summary>
        /// Returns the last fake health state.
        /// </summary>
        public SensorHealth GetHealth()
        {
            return _health;
        }
    }

    /// <summary>
    /// Test BMP280 sensor with switchable unavailable and read-failed modes.
    /// </summary>
    public sealed class FakeBmp280Sensor : ISensor<Bmp280Sample>
    {
        private readonly ITimeProvider _timeProvider;
        private SensorHealth _health;

        // Test switch for missing BMP280 scenario.
        public bool ForceNotDetected { get; set; }

        // Test switch for BMP280 read failure scenario.
        public bool ForceReadFailure { get; set; }

        /// <summary>
        /// Creates a fake BMP280 sensor.
        /// </summary>
        public FakeBmp280Sensor(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _health = SensorHealth.Unknown;
        }

        /// <summary>
        /// Initializes the fake BMP280 sensor.
        /// </summary>
        public SensorInitializationResult Initialize()
        {
            if (ForceNotDetected)
            {
                _health = SensorHealth.Failed;
                return SensorInitializationResult.NotDetected;
            }

            _health = SensorHealth.Ready;
            return SensorInitializationResult.Success;
        }

        /// <summary>
        /// Returns one fake BMP280 sample or a configured failure.
        /// </summary>
        public SensorReadResult<Bmp280Sample> Read()
        {
            if (ForceReadFailure || ForceNotDetected)
            {
                _health = SensorHealth.Failed;
                return SensorReadResult<Bmp280Sample>.Failure(SensorErrorCode.NotDetected, -10, "BMP280 neni dostupny.");
            }

            Bmp280Sample sample = new Bmp280Sample();
            sample.SampledAt = _timeProvider.GetNow().Value;
            sample.PressureHpa = 1013.25;
            sample.AuxiliaryTemperatureCelsius = 22.1;
            _health = SensorHealth.Ready;
            return SensorReadResult<Bmp280Sample>.Success(sample);
        }

        /// <summary>
        /// Returns the last fake health state.
        /// </summary>
        public SensorHealth GetHealth()
        {
            return _health;
        }
    }

    /// <summary>
    /// Test SPS30 sensor with switchable fan, laser, timeout and disabled modes.
    /// </summary>
    public sealed class FakeSps30Sensor : ISensor<Sps30Sample>
    {
        private readonly ITimeProvider _timeProvider;
        private SensorHealth _health;
        private bool _enabled;

        // Test switch for SPS30 fan diagnostics.
        public bool ForceFanError { get; set; }

        // Test switch for SPS30 laser diagnostics.
        public bool ForceLaserError { get; set; }

        // Test switch for SPS30 timeout scenario.
        public bool ForceReadFailure { get; set; }

        /// <summary>
        /// Creates a fake SPS30 sensor.
        /// </summary>
        public FakeSps30Sensor(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _health = SensorHealth.Unknown;
            _enabled = true;
        }

        /// <summary>
        /// Enables or disables the fake SPS30 sensor.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _health = enabled ? SensorHealth.Ready : SensorHealth.Disabled;
        }

        /// <summary>
        /// Initializes the fake SPS30 sensor.
        /// </summary>
        public SensorInitializationResult Initialize()
        {
            _health = SensorHealth.Ready;
            return SensorInitializationResult.Success;
        }

        /// <summary>
        /// Returns one fake SPS30 sample or a configured failure.
        /// </summary>
        public SensorReadResult<Sps30Sample> Read()
        {
            if (!_enabled)
            {
                _health = SensorHealth.Disabled;
                return SensorReadResult<Sps30Sample>.Failure(SensorErrorCode.DisabledOnBattery, 0, "SPS30 je vypnuty v bateriovem rezimu.");
            }

            if (ForceFanError)
            {
                _health = SensorHealth.Failed;
                return SensorReadResult<Sps30Sample>.Failure(SensorErrorCode.FanError, -20, "SPS30 hlasi chybu ventilatoru.");
            }

            if (ForceLaserError)
            {
                _health = SensorHealth.Failed;
                return SensorReadResult<Sps30Sample>.Failure(SensorErrorCode.LaserError, -21, "SPS30 hlasi chybu laseru.");
            }

            if (ForceReadFailure)
            {
                _health = SensorHealth.Failed;
                return SensorReadResult<Sps30Sample>.Failure(SensorErrorCode.Timeout, -22, "SPS30 neodpovedel v limitu.");
            }

            Sps30Sample sample = new Sps30Sample();
            sample.SampledAt = _timeProvider.GetNow().Value;
            sample.MassPm1 = 2.0;
            sample.MassPm25 = 4.5;
            sample.MassPm4 = 5.1;
            sample.MassPm10 = 6.0;
            sample.NumberPm05 = 30;
            sample.NumberPm1 = 12;
            sample.NumberPm25 = 3;
            sample.NumberPm4 = 1;
            sample.NumberPm10 = 0.2;
            sample.TypicalParticleSize = 0.6;
            _health = SensorHealth.Ready;
            return SensorReadResult<Sps30Sample>.Success(sample);
        }

        /// <summary>
        /// Returns the last fake health state.
        /// </summary>
        public SensorHealth GetHealth()
        {
            return _health;
        }
    }

    /// <summary>
    /// Test power monitor with switchable mains and battery voltage.
    /// </summary>
    public sealed class FakePowerMonitor : IPowerMonitor
    {
        private readonly ITimeProvider _timeProvider;

        // Test switch for simulating mains power loss.
        public bool MainsAvailable { get; set; }

        // Fake battery voltage returned by the power monitor.
        public double BatteryVoltage { get; set; }

        /// <summary>
        /// Creates a fake power monitor.
        /// </summary>
        public FakePowerMonitor(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            MainsAvailable = true;
            BatteryVoltage = 4.1;
        }

        /// <summary>
        /// Reads fake power state.
        /// </summary>
        public PowerState Read()
        {
            PowerState state = new PowerState();
            state.MainsAvailable = MainsAvailable;
            state.BatteryActive = !MainsAvailable;
            state.BatteryVoltage = BatteryVoltage;
            state.HasBatteryVoltage = true;
            state.BatteryLow = BatteryVoltage > 0 && BatteryVoltage < 3.5;
            return state;
        }
    }

    /// <summary>
    /// Test API client with switchable Wi-Fi availability.
    /// </summary>
    public sealed class FakeApiClient : IApiClient
    {
        // Test switch for simulating unavailable Wi-Fi or server.
        public bool WifiAvailable { get; set; }

        /// <summary>
        /// Creates a fake API client.
        /// </summary>
        public FakeApiClient()
        {
            WifiAvailable = true;
        }

        /// <summary>
        /// Uploads a fake measurement record.
        /// </summary>
        public bool UploadMeasurement(MeasurementRecord record)
        {
            return WifiAvailable;
        }

        /// <summary>
        /// Uploads a fake device event.
        /// </summary>
        public bool UploadEvent(DeviceEvent deviceEvent)
        {
            return WifiAvailable;
        }

        /// <summary>
        /// Registers fake station identity.
        /// </summary>
        public bool RegisterStation(StationIdentity identity)
        {
            return WifiAvailable && identity != null && identity.StationId != null;
        }

        /// <summary>
        /// Returns no fake configuration update by default.
        /// </summary>
        public bool TryDownloadConfiguration(StationIdentity identity, int currentConfigurationVersion, out RemoteConfigurationUpdate update)
        {
            update = new RemoteConfigurationUpdate();
            update.HasUpdate = false;
            update.ConfigurationVersion = currentConfigurationVersion;
            return WifiAvailable;
        }

        /// <summary>
        /// Resolves fake Wi-Fi access points to a deterministic test location.
        /// </summary>
        public bool TryResolveLocation(StationIdentity identity, WifiAccessPointObservation[] accessPoints, out StationLocation location)
        {
            location = new StationLocation();
            if (!WifiAvailable || accessPoints == null || accessPoints.Length < 2)
            {
                location.HasLocation = false;
                return false;
            }

            location.HasLocation = true;
            location.Latitude = 50.0755;
            location.Longitude = 14.4378;
            location.AccuracyMeters = 80;
            location.Source = LocationSource.WifiGeolocation;
            return true;
        }
    }

    /// <summary>
    /// Provides deterministic Wi-Fi scan results for location tests.
    /// </summary>
    public sealed class FakeWifiScanner : IWifiScanner
    {
        /// <summary>
        /// Scans fake nearby access points.
        /// </summary>
        public WifiAccessPointObservation[] Scan()
        {
            WifiAccessPointObservation first = new WifiAccessPointObservation();
            first.MacAddress = "00:11:22:33:44:55";
            first.SignalStrengthDbm = -45;
            first.Channel = 1;
            first.AgeMilliseconds = 0;

            WifiAccessPointObservation second = new WifiAccessPointObservation();
            second.MacAddress = "00:11:22:33:44:66";
            second.SignalStrengthDbm = -67;
            second.Channel = 6;
            second.AgeMilliseconds = 0;

            return new WifiAccessPointObservation[] { first, second };
        }
    }

    /// <summary>
    /// Test setup portal used until the real ESP32 access point backend is connected.
    /// </summary>
    public sealed class FakeProvisioningPortal : IProvisioningPortal
    {
        private StationConfiguration _pendingConfiguration;

        // Test switch for simulating a saved setup form.
        public bool SaveImmediately { get; set; }

        // Configuration returned when SaveImmediately is enabled.
        public StationConfiguration ConfigurationToSave { get; set; }

        /// <summary>
        /// Starts the fake setup portal.
        /// </summary>
        public bool Start(StationConfiguration configuration)
        {
            _pendingConfiguration = configuration;
            Debug.WriteLine("Fake provisioning portal pripraven.");
            return true;
        }

        /// <summary>
        /// Waits for a simulated save or returns timeout after the setup window.
        /// </summary>
        public ProvisioningWindowResult WaitForResult(int timeoutMilliseconds)
        {
            ProvisioningWindowResult result = new ProvisioningWindowResult();

            if (SaveImmediately)
            {
                result.ClientConnected = true;
                result.ConfigurationSaved = true;
                result.Configuration = ConfigurationToSave == null ? _pendingConfiguration : ConfigurationToSave;
                result.Message = "Fake provisioning portal ulozil testovaci konfiguraci.";
                return result;
            }

            result.ClientConnected = false;
            result.ConfigurationSaved = false;
            result.TimedOut = true;
            result.Configuration = _pendingConfiguration;
            result.Message = "Fake provisioning portal vyprsel bez ulozeni.";
            return result;
        }

        /// <summary>
        /// Waits for a simulated save or returns button cancellation.
        /// </summary>
        public ProvisioningWindowResult WaitForResultUntilCancelled(NanoTtgoButtons buttons)
        {
            ProvisioningWindowResult result = WaitForResult(0);
            if (!result.ConfigurationSaved)
            {
                result.TimedOut = false;
                result.CancelledByButton = true;
                result.Message = "Fake provisioning portal ukoncen tlacitkem.";
            }

            return result;
        }

        /// <summary>
        /// Stops the fake setup portal.
        /// </summary>
        public void Stop()
        {
            Debug.WriteLine("Fake provisioning portal zastaven.");
        }
    }
}
