using System;

namespace MeteoStanice
{
    /// <summary>
    /// Represents one boot-safe sensor sample used while firmware is restored in layers.
    /// </summary>
    public sealed class BootSensorSample
    {
        // Measured test temperature.
        public double TemperatureCelsius { get; set; }
        // Measured test relative humidity.
        public double RelativeHumidityPercent { get; set; }
        // Timestamp assigned to the sample.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Creates deterministic sensor samples without touching hardware drivers.
    /// </summary>
    public sealed class BootSensorSmokeService
    {
        private int _sequence;

        /// <summary>
        /// Reads one deterministic sample for debugger and metadata validation.
        /// </summary>
        public BootSensorReadResult Read()
        {
            _sequence++;

            BootSensorSample sample = new BootSensorSample();
            sample.TemperatureCelsius = 22.0 + (_sequence / 10.0);
            sample.RelativeHumidityPercent = 45.0;
            sample.SampledAt = new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(_sequence);

            BootSensorReadResult result = new BootSensorReadResult();
            result.IsSuccess = true;
            result.Value = sample;
            result.ErrorCode = SensorErrorCode.None;
            result.ErrorMessage = string.Empty;
            return result;
        }
    }

    /// <summary>
    /// Represents a boot-safe sensor read result without generic metadata.
    /// </summary>
    public sealed class BootSensorReadResult
    {
        // True when Value contains a valid sample.
        public bool IsSuccess { get; set; }
        // Sensor sample returned on success.
        public BootSensorSample Value { get; set; }
        // Normalized error code returned on failure.
        public SensorErrorCode ErrorCode { get; set; }
        // Short diagnostic message returned on failure.
        public string ErrorMessage { get; set; }
    }
}
