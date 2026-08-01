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
        // PM2.5 mass concentration.
        public double MassPm25 { get; set; }
        // PM10 mass concentration.
        public double MassPm10 { get; set; }
        // Typical particle size reported by SPS30.
        public double TypicalParticleSize { get; set; }
        // Timestamp assigned when the sample was captured.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Represents an SHT40 read result without generic metadata.
    /// </summary>
    public sealed class Sht40ReadResult
    {
        // True when Value contains a valid sample.
        public bool IsSuccess { get; set; }
        // SHT40 sample returned on success.
        public Sht40Sample Value { get; set; }
        // Normalized error code returned on failure.
        public SensorErrorCode ErrorCode { get; set; }
        // Short diagnostic message returned on failure.
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Represents a BMP280 read result without generic metadata.
    /// </summary>
    public sealed class Bmp280ReadResult
    {
        // True when Value contains a valid sample.
        public bool IsSuccess { get; set; }
        // BMP280 sample returned on success.
        public Bmp280Sample Value { get; set; }
        // Normalized error code returned on failure.
        public SensorErrorCode ErrorCode { get; set; }
        // Short diagnostic message returned on failure.
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Represents an SPS30 read result without generic metadata.
    /// </summary>
    public sealed class Sps30ReadResult
    {
        // True when Value contains a valid sample.
        public bool IsSuccess { get; set; }
        // SPS30 sample returned on success.
        public Sps30Sample Value { get; set; }
        // Normalized error code returned on failure.
        public SensorErrorCode ErrorCode { get; set; }
        // Short diagnostic message returned on failure.
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Provides deterministic fake sensor readings for layered firmware restore.
    /// </summary>
    public sealed class FakeNanoFriendlySensors
    {
        private int _sequence;

        /// <summary>
        /// Reads one fake SHT40 sample.
        /// </summary>
        public Sht40ReadResult ReadSht40()
        {
            DateTime now = NextTimestamp();
            Sht40Sample sample = new Sht40Sample();
            sample.TemperatureCelsius = 22.0 + (_sequence / 10.0);
            sample.RelativeHumidityPercent = 45.0;
            sample.SampledAt = now;
            sample.CrcOk = true;

            Sht40ReadResult result = new Sht40ReadResult();
            result.IsSuccess = true;
            result.Value = sample;
            result.ErrorCode = SensorErrorCode.None;
            result.ErrorMessage = string.Empty;
            return result;
        }

        /// <summary>
        /// Reads one fake BMP280 sample.
        /// </summary>
        public Bmp280ReadResult ReadBmp280()
        {
            DateTime now = NextTimestamp();
            Bmp280Sample sample = new Bmp280Sample();
            sample.PressureHpa = 1013.25;
            sample.AuxiliaryTemperatureCelsius = 22.1;
            sample.SampledAt = now;

            Bmp280ReadResult result = new Bmp280ReadResult();
            result.IsSuccess = true;
            result.Value = sample;
            result.ErrorCode = SensorErrorCode.None;
            result.ErrorMessage = string.Empty;
            return result;
        }

        /// <summary>
        /// Reads one fake SPS30 sample.
        /// </summary>
        public Sps30ReadResult ReadSps30()
        {
            DateTime now = NextTimestamp();
            Sps30Sample sample = new Sps30Sample();
            sample.MassPm25 = 4.5;
            sample.MassPm10 = 6.0;
            sample.TypicalParticleSize = 0.6;
            sample.SampledAt = now;

            Sps30ReadResult result = new Sps30ReadResult();
            result.IsSuccess = true;
            result.Value = sample;
            result.ErrorCode = SensorErrorCode.None;
            result.ErrorMessage = string.Empty;
            return result;
        }

        private DateTime NextTimestamp()
        {
            _sequence++;
            return new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(_sequence);
        }
    }
}
