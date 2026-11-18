namespace MeteoStanice
{
    /// <summary>
    /// Defines expected sensor read failures.
    /// </summary>
    public enum SensorErrorCode
    {
        // Successful read.
        None = 0,
        // Physical sensor was not found on its bus.
        NotDetected = 1,
        // Sensor did not respond before timeout.
        Timeout = 2,
        // Sensor frame was received but checksum was invalid.
        CrcFailed = 3,
        // Sensor returned data in unexpected format.
        InvalidResponse = 4,
        // I2C, UART or driver communication failed.
        CommunicationFailed = 5,
        // SPS30 reported fan failure.
        FanError = 6,
        // SPS30 reported laser failure.
        LaserError = 7,
        // Sensor was skipped intentionally to save battery.
        DisabledOnBattery = 8
    }

    /// <summary>
    /// Groups device events by subsystem.
    /// </summary>
    public enum DeviceEventCategory
    {
        // Sensor-related event.
        Sensor = 0,
        // Bus or protocol communication event.
        Communication = 1,
        // Wi-Fi or internet event.
        Network = 2,
        // Clock or NTP event.
        Time = 3,
        // Main power or battery event.
        Power = 4,
        // Flash or queue event.
        Storage = 5,
        // Local or remote configuration event.
        Configuration = 6,
        // Firmware lifecycle event.
        System = 7
    }

    /// <summary>
    /// Defines event severity for server-side processing.
    /// </summary>
    public enum DeviceEventSeverity
    {
        // Informational state transition.
        Information = 0,
        // Non-critical degraded state.
        Warning = 1,
        // Component or communication failure.
        Error = 2,
        // Data loss or device-critical condition.
        Critical = 3
    }

    /// <summary>
    /// Defines stable machine-readable event codes.
    /// </summary>
    public enum DeviceEventCode
    {
        // Firmware booted.
        DeviceStarted = 0,
        // Firmware restarted after previous run.
        DeviceRestarted = 1,
        // Main 5V source disappeared.
        MainsPowerLost = 2,
        // Main 5V source returned.
        MainsPowerRestored = 3,
        // Backup power became active.
        BackupPowerActivated = 4,
        // Battery voltage crossed low threshold.
        BatteryLow = 5,
        // Brownout condition was detected.
        BrownoutDetected = 6,
        // SHT40 was missing at initialization.
        Sht40NotDetected = 7,
        // SHT40 read operation failed.
        Sht40ReadFailed = 8,
        // SHT40 returned bad CRC.
        Sht40CrcFailed = 9,
        // SHT40 recovered after failure.
        Sht40Recovered = 10,
        // BMP280 was missing at initialization.
        Bmp280NotDetected = 11,
        // BMP280 read operation failed.
        Bmp280ReadFailed = 12,
        // BMP280 recovered after failure.
        Bmp280Recovered = 13,
        // SPS30 was missing at initialization.
        Sps30NotDetected = 14,
        // SPS30 read operation failed.
        Sps30ReadFailed = 15,
        // SPS30 reported fan diagnostics.
        Sps30FanError = 16,
        // SPS30 reported laser diagnostics.
        Sps30LaserError = 17,
        // SPS30 was intentionally stopped on battery.
        Sps30DisabledOnBattery = 18,
        // SPS30 recovered after failure.
        Sps30Restored = 19,
        // Wi-Fi connection attempt failed.
        WifiConnectionFailed = 20,
        // Wi-Fi connected successfully.
        WifiConnected = 21,
        // Server could not be reached.
        ServerUnavailable = 22,
        // Server connectivity recovered.
        ServerConnectionRestored = 23,
        // Server rejected credentials or token.
        AuthenticationFailed = 24,
        // Clock sync attempt failed.
        ClockSynchronizationFailed = 25,
        // Clock sync succeeded.
        ClockSynchronized = 26,
        // Queue is approaching capacity.
        LocalQueueNearFull = 27,
        // Queue has no free capacity.
        LocalQueueFull = 28,
        // Stored record failed validation.
        LocalRecordCorrupted = 29,
        // Configuration failed validation.
        ConfigurationInvalid = 30,
        // Configuration was saved and applied.
        ConfigurationApplied = 31
    }

}
