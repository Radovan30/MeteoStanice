namespace MeteoStanice
{
    /// <summary>
    /// Represents a sensor read result without using exceptions for expected failures.
    /// </summary>
    /// <typeparam name="T">Sample model returned on success.</typeparam>
    public sealed class SensorReadResult<T>
    {
        private SensorReadResult()
        {
        }

        // True when Value contains a valid sensor sample.
        public bool IsSuccess { get; private set; }

        // Sensor sample filled only for successful reads.
        public T Value { get; private set; }

        // Normalized failure code used by application services.
        public SensorErrorCode ErrorCode { get; private set; }

        // Original hardware or driver-specific code for diagnostics.
        public int RawErrorCode { get; private set; }

        // Short message suitable for logs and device events.
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Creates a successful sensor read result.
        /// </summary>
        public static SensorReadResult<T> Success(T value)
        {
            SensorReadResult<T> result = new SensorReadResult<T>();
            result.IsSuccess = true;
            result.Value = value;
            result.ErrorCode = SensorErrorCode.None;
            result.RawErrorCode = 0;
            result.ErrorMessage = string.Empty;
            return result;
        }

        /// <summary>
        /// Creates a failed sensor read result for an expected communication problem.
        /// </summary>
        public static SensorReadResult<T> Failure(SensorErrorCode errorCode, int rawErrorCode, string message)
        {
            SensorReadResult<T> result = new SensorReadResult<T>();
            result.IsSuccess = false;
            result.Value = default(T);
            result.ErrorCode = errorCode;
            result.RawErrorCode = rawErrorCode;
            result.ErrorMessage = message;
            return result;
        }
    }
}
