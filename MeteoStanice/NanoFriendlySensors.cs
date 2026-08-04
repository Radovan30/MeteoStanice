using System;

namespace MeteoStanice
{
    /// <summary>
    /// Reads nano-safe sensor results from the active sensor provider.
    /// </summary>
    public interface INanoFriendlySensors
    {
        /// <summary>
        /// Reads one SHT40 sample.
        /// </summary>
        Sht40ReadResult ReadSht40();

        /// <summary>
        /// Reads one BMP280 sample.
        /// </summary>
        Bmp280ReadResult ReadBmp280();

        /// <summary>
        /// Reads one SPS30 sample.
        /// </summary>
        Sps30ReadResult ReadSps30();
    }

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
        // Number concentration for particles larger than 0.5 um.
        public double NumberPm05 { get; set; }
        // Number concentration for particles larger than 1.0 um.
        public double NumberPm1 { get; set; }
        // Number concentration for particles larger than 2.5 um.
        public double NumberPm25 { get; set; }
        // Number concentration for particles larger than 4.0 um.
        public double NumberPm4 { get; set; }
        // Number concentration for particles larger than 10 um.
        public double NumberPm10 { get; set; }
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
    public sealed class FakeNanoFriendlySensors : INanoFriendlySensors
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
            sample.MassPm1 = 2.0;
            sample.MassPm25 = 4.5;
            sample.MassPm4 = 5.1;
            sample.MassPm10 = 6.0;
            sample.NumberPm05 = 8.0;
            sample.NumberPm1 = 5.0;
            sample.NumberPm25 = 3.0;
            sample.NumberPm4 = 1.0;
            sample.NumberPm10 = 0.2;
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
