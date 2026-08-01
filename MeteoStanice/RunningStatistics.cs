namespace MeteoStanice
{
    /// <summary>
    /// Calculates count, mean, min, max and standard deviation incrementally.
    /// </summary>
    public sealed class RunningStatistics
    {
        // Expected count is copied to output for completeness checks.
        private readonly int _expectedSampleCount;
        // Counters distinguish valid data from failed reads.
        private int _receivedCount;
        private int _validCount;
        private int _failedCount;
        private double _mean;
        private double _m2;
        private double _minimum;
        private double _maximum;
        private bool _hasValue;

        /// <summary>
        /// Creates an accumulator with the expected sample count for the interval.
        /// </summary>
        public RunningStatistics(int expectedSampleCount)
        {
            // The expected count comes from station configuration.
            _expectedSampleCount = expectedSampleCount;
        }

        /// <summary>
        /// Adds one valid value using Welford variance update.
        /// </summary>
        public void Add(double value)
        {
            // A valid reading increments both received and valid counters.
            _receivedCount++;
            _validCount++;

            if (!_hasValue)
            {
                // First value initializes min, max and mean.
                _hasValue = true;
                _mean = value;
                _minimum = value;
                _maximum = value;
                _m2 = 0;
                return;
            }

            // Min and max are updated before the variance calculation.
            if (value < _minimum)
            {
                _minimum = value;
            }

            if (value > _maximum)
            {
                _maximum = value;
            }

            // Welford update calculates variance incrementally.
            double delta = value - _mean;
            _mean = _mean + delta / _validCount;
            double delta2 = value - _mean;
            _m2 = _m2 + delta * delta2;
        }

        /// <summary>
        /// Adds one failed sample without inventing a numeric value.
        /// </summary>
        public void AddFailure()
        {
            // Failed reads are counted without adding a fake numeric value.
            _receivedCount++;
            _failedCount++;
        }

        /// <summary>
        /// Converts the current accumulator state to a domain aggregate.
        /// </summary>
        public AggregatedValue ToValue()
        {
            // Convert internal state to the transfer model used in MeasurementRecord.
            AggregatedValue value = new AggregatedValue();
            value.ExpectedSampleCount = _expectedSampleCount;
            value.ReceivedSampleCount = _receivedCount;
            value.ValidSampleCount = _validCount;
            value.FailedSampleCount = _failedCount;
            value.HasValue = _hasValue;

            if (_hasValue)
            {
                // Standard deviation is meaningful only with at least two valid values.
                value.Mean = _mean;
                value.Minimum = _minimum;
                value.Maximum = _maximum;
                value.StandardDeviation = _validCount > 1 ? SquareRoot(_m2 / (_validCount - 1)) : 0;
            }

            return value;
        }

        private static double SquareRoot(double value)
        {
            // nanoFramework core profile may not include System.Math.Sqrt.
            if (value <= 0)
            {
                return 0;
            }

            double estimate = value;
            int iteration = 0;

            while (iteration < 8)
            {
                // Newton iteration is enough for measurement statistics.
                estimate = 0.5 * (estimate + value / estimate);
                iteration++;
            }

            return estimate;
        }

        /// <summary>
        /// Clears all accumulated state for the next interval.
        /// </summary>
        public void Reset()
        {
            // Reset every field so the next interval starts cleanly.
            _receivedCount = 0;
            _validCount = 0;
            _failedCount = 0;
            _mean = 0;
            _m2 = 0;
            _minimum = 0;
            _maximum = 0;
            _hasValue = false;
        }
    }
}
