namespace MeteoStanice
{
    /// <summary>
    /// Defines expected sensor read failures.
    /// </summary>
    public enum SensorErrorCode
    {
        // Successful read.
        None,
        // Physical sensor was not found on its bus.
        NotDetected,
        // Sensor did not respond before timeout.
        Timeout,
        // Sensor frame was received but checksum was invalid.
        CrcFailed,
        // Sensor returned data in unexpected format.
        InvalidResponse,
        // I2C, UART or driver communication failed.
        CommunicationFailed,
        // SPS30 reported fan failure.
        FanError,
        // SPS30 reported laser failure.
        LaserError,
        // Sensor was skipped intentionally to save battery.
        DisabledOnBattery
    }

    /// <summary>
    /// Describes current sensor availability.
    /// </summary>
    public enum SensorHealth
    {
        // Sensor has not been initialized or read yet.
        Unknown,
        // Sensor is initialized and ready.
        Ready,
        // Sensor works but recently showed errors.
        Degraded,
        // Sensor cannot be used.
        Failed,
        // Sensor is intentionally not running.
        Disabled
    }

    /// <summary>
    /// Describes sensor initialization outcome.
    /// </summary>
    public enum SensorInitializationResult
    {
        // Hardware initialization succeeded.
        Success,
        // Hardware address or response was missing.
        NotDetected,
        // Bus communication failed during setup.
        CommunicationFailed,
        // Real driver is not implemented in this firmware build.
        Unsupported
    }

    /// <summary>
    /// Groups device events by subsystem.
    /// </summary>
    public enum DeviceEventCategory
    {
        // Sensor-related event.
        Sensor,
        // Bus or protocol communication event.
        Communication,
        // Wi-Fi or internet event.
        Network,
        // Clock or NTP event.
        Time,
        // Main power or battery event.
        Power,
        // Flash or queue event.
        Storage,
        // Local or remote configuration event.
        Configuration,
        // Firmware lifecycle event.
        System
    }

    /// <summary>
    /// Defines event severity for server-side processing.
    /// </summary>
    public enum DeviceEventSeverity
    {
        // Informational state transition.
        Information,
        // Non-critical degraded state.
        Warning,
        // Component or communication failure.
        Error,
        // Data loss or device-critical condition.
        Critical
    }

    /// <summary>
    /// Defines stable machine-readable event codes.
    /// </summary>
    public enum DeviceEventCode
    {
        // Firmware booted.
        DeviceStarted,
        // Firmware restarted after previous run.
        DeviceRestarted,
        // Main 5V source disappeared.
        MainsPowerLost,
        // Main 5V source returned.
        MainsPowerRestored,
        // Backup power became active.
        BackupPowerActivated,
        // Battery voltage crossed low threshold.
        BatteryLow,
        // Brownout condition was detected.
        BrownoutDetected,
        // SHT40 was missing at initialization.
        Sht40NotDetected,
        // SHT40 read operation failed.
        Sht40ReadFailed,
        // SHT40 returned bad CRC.
        Sht40CrcFailed,
        // SHT40 recovered after failure.
        Sht40Recovered,
        // BMP280 was missing at initialization.
        Bmp280NotDetected,
        // BMP280 read operation failed.
        Bmp280ReadFailed,
        // BMP280 recovered after failure.
        Bmp280Recovered,
        // SPS30 was missing at initialization.
        Sps30NotDetected,
        // SPS30 read operation failed.
        Sps30ReadFailed,
        // SPS30 reported fan diagnostics.
        Sps30FanError,
        // SPS30 reported laser diagnostics.
        Sps30LaserError,
        // SPS30 was intentionally stopped on battery.
        Sps30DisabledOnBattery,
        // SPS30 recovered after failure.
        Sps30Restored,
        // Wi-Fi connection attempt failed.
        WifiConnectionFailed,
        // Wi-Fi connected successfully.
        WifiConnected,
        // Server could not be reached.
        ServerUnavailable,
        // Server connectivity recovered.
        ServerConnectionRestored,
        // Server rejected credentials or token.
        AuthenticationFailed,
        // Clock sync attempt failed.
        ClockSynchronizationFailed,
        // Clock sync succeeded.
        ClockSynchronized,
        // Queue is approaching capacity.
        LocalQueueNearFull,
        // Queue has no free capacity.
        LocalQueueFull,
        // Stored record failed validation.
        LocalRecordCorrupted,
        // Configuration failed validation.
        ConfigurationInvalid,
        // Configuration was saved and applied.
        ConfigurationApplied
    }

    /// <summary>
    /// Describes quality and limitations of one measurement interval.
    /// </summary>
    public enum MeasurementStatus
    {
        // Interval contains enough valid data.
        Ok,
        // Too many samples were missing.
        InsufficientSamples,
        // SHT40 failed during this interval.
        Sht40ReadFailed,
        // BMP280 failed during this interval.
        Bmp280ReadFailed,
        // SPS30 failed during this interval.
        Sps30ReadFailed,
        // Backup battery was active during this interval.
        BackupPowerActive,
        // SPS30 was intentionally skipped on battery.
        Sps30DisabledOnBattery
    }

    /// <summary>
    /// Describes how station location was obtained.
    /// </summary>
    public enum LocationSource
    {
        // Location has not been resolved.
        Unknown,
        // Operator entered the location.
        Manual,
        // Server estimated location from Wi-Fi scan.
        WifiGeolocation,
        // GPS receiver provided the location.
        Gps,
        // Server estimated location from internet address.
        IpAddress
    }
}
