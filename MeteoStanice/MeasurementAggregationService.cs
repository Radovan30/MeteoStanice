using System;

namespace MeteoStanice
{
    /// <summary>
    /// Aggregates sensor samples for one measurement interval.
    /// </summary>
    public sealed class MeasurementAggregationService
    {
        private readonly int _expectedSampleCount;
        private readonly RunningStatistics _temperature;
        private readonly RunningStatistics _humidity;
        private readonly RunningStatistics _pressure;
        private readonly RunningStatistics _pm25;
        private DateTime _startedAt;
        private MeasurementStatus _status;

        /// <summary>
        /// Creates the aggregation service for a fixed interval size.
        /// </summary>
        public MeasurementAggregationService(int expectedSampleCount)
        {
            _expectedSampleCount = expectedSampleCount;
            _temperature = new RunningStatistics(expectedSampleCount);
            _humidity = new RunningStatistics(expectedSampleCount);
            _pressure = new RunningStatistics(expectedSampleCount);
            _pm25 = new RunningStatistics(expectedSampleCount);
            Reset(DateTime.MinValue);
        }

        /// <summary>
        /// Starts a new aggregation interval.
        /// </summary>
        public void Reset(DateTime startedAt)
        {
            _startedAt = startedAt;
            _status = MeasurementStatus.Ok;
            _temperature.Reset();
            _humidity.Reset();
            _pressure.Reset();
            _pm25.Reset();
        }

        /// <summary>
        /// Adds one SHT40 read result.
        /// </summary>
        public void AddSht40(SensorReadResult<Sht40Sample> result)
        {
            // SHT40 contributes two independent aggregates: temperature and humidity.
            if (result.IsSuccess)
            {
                Sht40Sample sample = result.Value;
                if (sample.CrcOk)
                {
                    // Valid values update running statistics without storing raw samples.
                    _temperature.Add(sample.TemperatureCelsius);
                    _humidity.Add(sample.RelativeHumidityPercent);
                }
                else
                {
                    // Missing values are counted as failures, not represented as zero.
                    _temperature.AddFailure();
                    _humidity.AddFailure();
                    AddStatus(MeasurementStatus.Sht40ReadFailed);
                }
            }
            else
            {
                // Expected sensor failures are reflected in aggregate quality counters.
                _temperature.AddFailure();
                _humidity.AddFailure();
                AddStatus(MeasurementStatus.Sht40ReadFailed);
            }
        }

        /// <summary>
        /// Adds one BMP280 read result.
        /// </summary>
        public void AddBmp280(SensorReadResult<Bmp280Sample> result)
        {
            // BMP280 pressure is aggregated independently from its auxiliary temperature.
            if (result.IsSuccess)
            {
                Bmp280Sample sample = result.Value;
                _pressure.Add(sample.PressureHpa);
            }
            else
            {
                _pressure.AddFailure();
                AddStatus(MeasurementStatus.Bmp280ReadFailed);
            }
        }

        /// <summary>
        /// Adds one SPS30 read result.
        /// </summary>
        public void AddSps30(SensorReadResult<Sps30Sample> result)
        {
            // Current firmware stores PM2.5 aggregate; more SPS30 aggregates can be added here.
            if (result.IsSuccess)
            {
                Sps30Sample sample = result.Value;
                _pm25.Add(sample.MassPm25);
            }
            else
            {
                _pm25.AddFailure();
                AddStatus(result.ErrorCode == SensorErrorCode.DisabledOnBattery
                    ? MeasurementStatus.Sps30DisabledOnBattery
                    : MeasurementStatus.Sps30ReadFailed);
            }
        }

        /// <summary>
        /// Adds a measurement status if the interval is still marked as OK.
        /// </summary>
        public void AddStatus(MeasurementStatus status)
        {
            // Keep the first non-OK status as the main reason for degraded measurement quality.
            if (_status == MeasurementStatus.Ok)
            {
                _status = status;
            }
        }

        /// <summary>
        /// Closes the current interval and returns an uploadable measurement record.
        /// </summary>
        public MeasurementRecord CloseInterval(string stationId, long sequenceNumber, DateTime endedAt, bool clockSynchronized)
        {
            MeasurementRecord record = new MeasurementRecord();
            record.RecordId = stationId + "-" + sequenceNumber;
            record.StationId = stationId;
            record.SequenceNumber = sequenceNumber;
            record.StartedAt = _startedAt;
            record.EndedAt = endedAt;
            record.TemperatureCelsius = _temperature.ToValue();
            record.RelativeHumidityPercent = _humidity.ToValue();
            record.PressureHpa = _pressure.ToValue();
            record.Pm25 = _pm25.ToValue();
            record.ClockSynchronized = clockSynchronized;
            record.Status = ResolveStatus(record);
            return record;
        }

        private MeasurementStatus ResolveStatus(MeasurementRecord record)
        {
            // Explicit sensor or power status has priority over sample-count validation.
            if (_status != MeasurementStatus.Ok)
            {
                return _status;
            }

            // Half of expected samples is the first conservative threshold for usable data.
            if (record.TemperatureCelsius.ValidSampleCount < _expectedSampleCount / 2)
            {
                return MeasurementStatus.InsufficientSamples;
            }

            return MeasurementStatus.Ok;
        }
    }
}
