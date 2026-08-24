namespace MeteoStanice
{
    /// <summary>
    /// Computes compact interval statistics without storing all raw samples.
    /// </summary>
    public sealed class NanoRunningStatistics
    {
        private int _count;
        private double _sum;
        private double _sumSquares;
        private double _minimum;
        private double _maximum;

        // Number of valid samples included in the statistics.
        public int Count
        {
            get
            {
                return _count;
            }
        }

        // Smallest value observed in the interval.
        public double Minimum
        {
            get
            {
                return _count == 0 ? 0 : _minimum;
            }
        }

        // Largest value observed in the interval.
        public double Maximum
        {
            get
            {
                return _count == 0 ? 0 : _maximum;
            }
        }

        /// <summary>
        /// Adds one valid numeric sample.
        /// </summary>
        public void Add(double value)
        {
            if (_count == 0)
            {
                _minimum = value;
                _maximum = value;
            }
            else
            {
                if (value < _minimum)
                {
                    _minimum = value;
                }

                if (value > _maximum)
                {
                    _maximum = value;
                }
            }

            _count++;
            _sum += value;
            _sumSquares += value * value;
        }

        /// <summary>
        /// Returns arithmetic mean or zero when the interval has no value.
        /// </summary>
        public double Mean()
        {
            return _count == 0 ? 0 : _sum / _count;
        }

        /// <summary>
        /// Returns population standard deviation for the interval.
        /// </summary>
        public double StandardDeviation()
        {
            if (_count < 2)
            {
                return 0;
            }

            double mean = Mean();
            double variance = (_sumSquares / _count) - (mean * mean);
            return variance <= 0 ? 0 : SquareRoot(variance);
        }

        /// <summary>
        /// Clears all accumulated statistics.
        /// </summary>
        public void Reset()
        {
            _count = 0;
            _sum = 0;
            _sumSquares = 0;
            _minimum = 0;
            _maximum = 0;
        }

        private static double SquareRoot(double value)
        {
            if (value <= 0)
            {
                return 0;
            }

            double estimate = value;
            for (int i = 0; i < 8; i++)
            {
                estimate = 0.5 * (estimate + value / estimate);
            }

            return estimate;
        }
    }
}
