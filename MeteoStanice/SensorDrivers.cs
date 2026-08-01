namespace MeteoStanice
{
    /// <summary>
    /// Real SHT40 driver boundary; I2C details are implemented here.
    /// </summary>
    public sealed class Sht40Sensor : ISensor<Sht40Sample>
    {
        /// <summary>
        /// Initializes the SHT40 hardware.
        /// </summary>
        public SensorInitializationResult Initialize()
        {
            // Placeholder until I2C command sequence is implemented.
            return SensorInitializationResult.Unsupported;
        }

        /// <summary>
        /// Reads one SHT40 sample.
        /// </summary>
        public SensorReadResult<Sht40Sample> Read()
        {
            // Expected driver absence is reported as a failed read result.
            return SensorReadResult<Sht40Sample>.Failure(SensorErrorCode.CommunicationFailed, 0, "Realny SHT40 ovladac zatim ceka na doplneni I2C implementace.");
        }

        /// <summary>
        /// Returns the last known SHT40 health.
        /// </summary>
        public SensorHealth GetHealth()
        {
            // Real driver will return the last health tracked during reads.
            return SensorHealth.Unknown;
        }
    }

    /// <summary>
    /// Real BMP280 driver boundary; I2C details are implemented here.
    /// </summary>
    public sealed class Bmp280Sensor : ISensor<Bmp280Sample>
    {
        /// <summary>
        /// Initializes the BMP280 hardware.
        /// </summary>
        public SensorInitializationResult Initialize()
        {
            // Placeholder until BMP280 chip ID and calibration reads are implemented.
            return SensorInitializationResult.Unsupported;
        }

        /// <summary>
        /// Reads one BMP280 sample.
        /// </summary>
        public SensorReadResult<Bmp280Sample> Read()
        {
            // Expected driver absence is reported as a failed read result.
            return SensorReadResult<Bmp280Sample>.Failure(SensorErrorCode.CommunicationFailed, 0, "Realny BMP280 ovladac zatim ceka na doplneni I2C implementace.");
        }

        /// <summary>
        /// Returns the last known BMP280 health.
        /// </summary>
        public SensorHealth GetHealth()
        {
            // Real driver will return the last health tracked during reads.
            return SensorHealth.Unknown;
        }
    }

    /// <summary>
    /// Real SPS30 driver boundary; UART or I2C details are implemented here.
    /// </summary>
    public sealed class Sps30Sensor : ISensor<Sps30Sample>
    {
        /// <summary>
        /// Initializes the SPS30 hardware.
        /// </summary>
        public SensorInitializationResult Initialize()
        {
            // Placeholder until SPS30 start-measurement sequence is implemented.
            return SensorInitializationResult.Unsupported;
        }

        /// <summary>
        /// Reads one SPS30 sample.
        /// </summary>
        public SensorReadResult<Sps30Sample> Read()
        {
            // Expected driver absence is reported as a failed read result.
            return SensorReadResult<Sps30Sample>.Failure(SensorErrorCode.CommunicationFailed, 0, "Realny SPS30 ovladac zatim ceka na doplneni UART/I2C implementace.");
        }

        /// <summary>
        /// Returns the last known SPS30 health.
        /// </summary>
        public SensorHealth GetHealth()
        {
            // Real driver will return the last health tracked during reads.
            return SensorHealth.Unknown;
        }
    }
}
