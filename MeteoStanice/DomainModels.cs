using System;

namespace MeteoStanice
{
    /// <summary>
    /// Represents one temperature and humidity reading from SHT40.
    /// </summary>
    public sealed class Sht40Sample
    {
        // Measured air temperature.
        public double TemperatureCelsius { get; set; }
        // Measured relative humidity.
        public double RelativeHumidityPercent { get; set; }
        // Timestamp assigned when the sample was captured.
        public DateTime SampledAt { get; set; }
        // True when the received sensor frame passed CRC validation.
        public bool CrcOk { get; set; }
    }

    /// <summary>
    /// Represents one pressure reading from BMP280.
    /// </summary>
    public sealed class Bmp280Sample
    {
        // Atmospheric pressure measured by BMP280.
        public double PressureHpa { get; set; }
        // Temperature reported by BMP280 and used only as an auxiliary value.
        public double AuxiliaryTemperatureCelsius { get; set; }
        // Timestamp assigned when the sample was captured.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Represents one particulate matter reading from SPS30.
    /// </summary>
    public sealed class Sps30Sample
    {
        // PM1.0 mass concentration.
        public double MassPm1 { get; set; }
        // PM2.5 mass concentration.
        public double MassPm25 { get; set; }
        // PM4.0 mass concentration.
        public double MassPm4 { get; set; }
        // PM10 mass concentration.
        public double MassPm10 { get; set; }
        // Number concentration for particles larger than 0.5 micrometer.
        public double NumberPm05 { get; set; }
        // Number concentration for particles larger than 1.0 micrometer.
        public double NumberPm1 { get; set; }
        // Number concentration for particles larger than 2.5 micrometer.
        public double NumberPm25 { get; set; }
        // Number concentration for particles larger than 4.0 micrometer.
        public double NumberPm4 { get; set; }
        // Number concentration for particles larger than 10 micrometer.
        public double NumberPm10 { get; set; }
        // Typical particle size reported by SPS30.
        public double TypicalParticleSize { get; set; }
        // Timestamp assigned when the sample was captured.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Groups environment samples captured in one measurement cycle.
    /// </summary>
    public sealed class MeteoSample
    {
        // SHT40 sample captured in the cycle.
        public Sht40Sample Sht40 { get; set; }
        // BMP280 sample captured in the cycle.
        public Bmp280Sample Bmp280 { get; set; }
        // Shared cycle timestamp.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Groups particle samples captured in one measurement cycle.
    /// </summary>
    public sealed class ParticleSample
    {
        // SPS30 sample captured in the cycle.
        public Sps30Sample Sps30 { get; set; }
        // Shared cycle timestamp.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Stores aggregated statistics without using zero as a missing-value marker.
    /// </summary>
    public sealed class AggregatedValue
    {
        // Arithmetic mean; valid only when HasValue is true.
        public double Mean { get; set; }
        // Minimum valid value in the interval.
        public double Minimum { get; set; }
        // Maximum valid value in the interval.
        public double Maximum { get; set; }
        // Sample standard deviation.
        public double StandardDeviation { get; set; }
        // Expected number of samples in the interval.
        public int ExpectedSampleCount { get; set; }
        // Received sample count including failed reads.
        public int ReceivedSampleCount { get; set; }
        // Count of valid numeric samples.
        public int ValidSampleCount { get; set; }
        // Count of failed sensor reads.
        public int FailedSampleCount { get; set; }
        // False means no valid numeric value exists.
        public bool HasValue { get; set; }
    }

    /// <summary>
    /// Represents one closed measurement interval waiting for upload.
    /// </summary>
    public sealed class MeasurementRecord
    {
        // Stable identity of one closed aggregation interval.
        public string RecordId { get; set; }
        // Station that produced the record.
        public string StationId { get; set; }
        // Monotonic station-local sequence number.
        public long SequenceNumber { get; set; }
        // Interval start time.
        public DateTime StartedAt { get; set; }
        // Interval end time.
        public DateTime EndedAt { get; set; }
        // Aggregated temperature.
        public AggregatedValue TemperatureCelsius { get; set; }
        // Aggregated relative humidity.
        public AggregatedValue RelativeHumidityPercent { get; set; }
        // Aggregated atmospheric pressure.
        public AggregatedValue PressureHpa { get; set; }
        // Aggregated PM2.5 particle value.
        public AggregatedValue Pm25 { get; set; }
        // Quality status for this interval.
        public MeasurementStatus Status { get; set; }
        // True when clock was synchronized while creating the record.
        public bool ClockSynchronized { get; set; }
    }

    /// <summary>
    /// Represents one operational or error event produced by the station.
    /// </summary>
    public sealed class DeviceEvent
    {
        // Stable event identity used for de-duplication.
        public string EventId { get; set; }
        // Station that produced the event.
        public string StationId { get; set; }
        // Monotonic event sequence number.
        public long SequenceNumber { get; set; }
        // First occurrence time.
        public DateTime OccurredAt { get; set; }
        // nanoFramework-friendly replacement for nullable EndedAt.
        public bool HasEndedAt { get; set; }
        // Event end time when HasEndedAt is true.
        public DateTime EndedAt { get; set; }
        // Event severity.
        public DeviceEventSeverity Severity { get; set; }
        // Event subsystem category.
        public DeviceEventCategory Category { get; set; }
        // Stable machine-readable event code.
        public DeviceEventCode Code { get; set; }
        // Component that produced the event.
        public string Component { get; set; }
        // Original hardware or driver error code.
        public int RawErrorCode { get; set; }
        // Short diagnostic message.
        public string Message { get; set; }
        // True when clock was synchronized while creating the event.
        public bool ClockSynchronized { get; set; }
        // Firmware version that created the event.
        public string FirmwareVersion { get; set; }
        // Last repeated occurrence time.
        public DateTime LastOccurredAt { get; set; }
        // Number of repeated occurrences.
        public int OccurrenceCount { get; set; }
        // True when this event marks recovery.
        public bool IsRecovery { get; set; }
    }

    /// <summary>
    /// Contains station settings loaded from persistent memory or defaults.
    /// </summary>
    public sealed class StationConfiguration
    {
        // User-facing station identifier used in the web application.
        public string StationId { get; set; }
        // Immutable hardware identifier for the physical board.
        public string DeviceHardwareId { get; set; }
        // Human-friendly station name.
        public string StationName { get; set; }
        // Firmware version currently running on the station.
        public string FirmwareVersion { get; set; }
        // Sampling period for one measurement cycle.
        public int SamplePeriodMilliseconds { get; set; }
        // Aggregation interval length in seconds.
        public int AggregationIntervalSeconds { get; set; }
        // Expected sample count in one aggregation interval.
        public int ExpectedSamplesPerInterval { get; set; }
        // Allows or forbids SPS30 fan operation on battery power.
        public bool Sps30EnabledOnBattery { get; set; }
        // Maximum number of queued measurements or events.
        public int LocalQueueCapacity { get; set; }
        // Wi-Fi network name.
        public string WifiSsid { get; set; }
        // Wi-Fi network password.
        public string WifiPassword { get; set; }
        // Server API base URL.
        public string ApiBaseUrl { get; set; }
        // True when IP address should be assigned by DHCP.
        public bool UseDhcp { get; set; }
        // Static IP address used when DHCP is disabled.
        public string StaticIpAddress { get; set; }
        // Gateway address used when DHCP is disabled.
        public string GatewayAddress { get; set; }
        // Subnet mask used when DHCP is disabled.
        public string SubnetMask { get; set; }
        // DNS server address used when DHCP is disabled.
        public string DnsAddress { get; set; }
        // Name of the temporary setup access point.
        public string ProvisioningAccessPointName { get; set; }
        // Password for the temporary setup access point and setup form.
        public string ProvisioningPassword { get; set; }
        // Local web address used by the setup portal.
        public string ProvisioningAddress { get; set; }
        // Setup window length after boot.
        public int ProvisioningWindowMilliseconds { get; set; }
        // Configuration schema or server revision.
        public int ConfigurationVersion { get; set; }
        // Allows the server to change station configuration remotely.
        public bool RemoteConfigurationEnabled { get; set; }
        // Period for checking remote configuration changes.
        public int RemoteConfigurationCheckIntervalSeconds { get; set; }
        // Last known station location.
        public StationLocation LastKnownLocation { get; set; }
    }

    /// <summary>
    /// Describes an immutable station identity sent to the server.
    /// </summary>
    public sealed class StationIdentity
    {
        // User-facing station identifier.
        public string StationId { get; set; }
        // Immutable hardware identifier.
        public string DeviceHardwareId { get; set; }
        // Human-friendly station name.
        public string StationName { get; set; }
        // Firmware version currently running on the station.
        public string FirmwareVersion { get; set; }
    }

    /// <summary>
    /// Represents a configuration update downloaded from the server.
    /// </summary>
    public sealed class RemoteConfigurationUpdate
    {
        // True when the server returned a newer configuration.
        public bool HasUpdate { get; set; }
        // Server configuration revision.
        public int ConfigurationVersion { get; set; }
        // New station configuration.
        public StationConfiguration Configuration { get; set; }
    }

    /// <summary>
    /// Describes one Wi-Fi access point observed during scanning.
    /// </summary>
    public sealed class WifiAccessPointObservation
    {
        // BSSID or access point MAC address.
        public string MacAddress { get; set; }
        // Signal strength in dBm.
        public int SignalStrengthDbm { get; set; }
        // Wi-Fi channel.
        public int Channel { get; set; }
        // Observation age in milliseconds.
        public int AgeMilliseconds { get; set; }
    }

    /// <summary>
    /// Stores an estimated station location and its source.
    /// </summary>
    public sealed class StationLocation
    {
        // True when Latitude and Longitude contain a meaningful estimate.
        public bool HasLocation { get; set; }
        // Latitude in degrees.
        public double Latitude { get; set; }
        // Longitude in degrees.
        public double Longitude { get; set; }
        // Accuracy radius in meters.
        public double AccuracyMeters { get; set; }
        // Source of the location estimate.
        public LocationSource Source { get; set; }
        // Time when the location was estimated.
        public DateTime EstimatedAt { get; set; }
    }

    /// <summary>
    /// Describes the current power source and battery state.
    /// </summary>
    public sealed class PowerState
    {
        // True when main 5V power is available.
        public bool MainsAvailable { get; set; }
        // True when backup battery is powering the station.
        public bool BatteryActive { get; set; }
        // Measured battery voltage.
        public double BatteryVoltage { get; set; }
        // True when BatteryVoltage contains a measured value.
        public bool HasBatteryVoltage { get; set; }
        // True when battery voltage is below configured threshold.
        public bool BatteryLow { get; set; }
    }

    /// <summary>
    /// Describes Wi-Fi and server connectivity.
    /// </summary>
    public sealed class NetworkState
    {
        // True when station is connected to Wi-Fi.
        public bool WifiConnected { get; set; }
        // True when server is reachable.
        public bool ServerAvailable { get; set; }
        // Assigned or configured IP address.
        public string IpAddress { get; set; }
    }

    /// <summary>
    /// Describes an expected sensor communication failure.
    /// </summary>
    public sealed class SensorReadError
    {
        // Component that produced the error.
        public string Component { get; set; }
        // Normalized sensor error code.
        public SensorErrorCode ErrorCode { get; set; }
        // Original hardware or driver error code.
        public int RawErrorCode { get; set; }
        // Short diagnostic message.
        public string Message { get; set; }
        // Time when the error occurred.
        public DateTime OccurredAt { get; set; }
    }

    /// <summary>
    /// Describes the outcome of the temporary startup setup portal.
    /// </summary>
    public sealed class ProvisioningWindowResult
    {
        // True when a user connected to the setup portal.
        public bool ClientConnected { get; set; }
        // True when a valid configuration was saved before timeout.
        public bool ConfigurationSaved { get; set; }
        // True when the setup window elapsed without saving.
        public bool TimedOut { get; set; }
        // Configuration saved through the setup portal.
        public StationConfiguration Configuration { get; set; }
        // Short diagnostic message written by the portal implementation.
        public string Message { get; set; }
    }
}
