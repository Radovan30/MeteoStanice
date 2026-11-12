using System;
using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Holds one raw set of readings captured during a runtime measurement cycle.
    /// </summary>
    public sealed class NanoSensorReadSet
    {
        // SHT40 temperature and humidity read result.
        public Sht40ReadResult Sht40 { get; set; }
        // BMP280 pressure read result.
        public Bmp280ReadResult Bmp280 { get; set; }
        // SPS30 particulate matter read result.
        public Sps30ReadResult Sps30 { get; set; }
        // Time when the read set was captured.
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Reads sensors through methods that can later be backed by real hardware drivers.
    /// </summary>
    public sealed class NanoSensorReader
    {
        private readonly INanoFriendlySensors _sensors;

        /// <summary>
        /// Creates a sensor reader over the currently available nano-safe sensor provider.
        /// </summary>
        public NanoSensorReader(INanoFriendlySensors sensors)
        {
            _sensors = sensors;
        }

        /// <summary>
        /// Reads all sensors that participate in one meteo sample.
        /// </summary>
        public NanoSensorReadSet ReadCurrentSample(bool batteryActive)
        {
            NanoSensorReadSet readSet = new NanoSensorReadSet();
            readSet.SampledAt = DateTime.UtcNow;
            readSet.Sht40 = ReadSht40();
            readSet.Bmp280 = ReadBmp280();
            readSet.Sps30 = batteryActive ? CreateSps30BatterySkip() : ReadSps30();
            return readSet;
        }

        /// <summary>
        /// Reads temperature and humidity.
        /// </summary>
        public Sht40ReadResult ReadSht40()
        {
            return _sensors.ReadSht40();
        }

        /// <summary>
        /// Reads atmospheric pressure.
        /// </summary>
        public Bmp280ReadResult ReadBmp280()
        {
            return _sensors.ReadBmp280();
        }

        /// <summary>
        /// Reads particulate matter.
        /// </summary>
        public Sps30ReadResult ReadSps30()
        {
            return _sensors.ReadSps30();
        }

        private static Sps30ReadResult CreateSps30BatterySkip()
        {
            Sps30ReadResult result = new Sps30ReadResult();
            result.IsSuccess = false;
            result.Value = null;
            result.ErrorCode = SensorErrorCode.DisabledOnBattery;
            result.ErrorMessage = "SPS30 vypnuty pri behu na baterii.";
            return result;
        }
    }

    /// <summary>
    /// Aggregates samples and emits one final record after the configured interval.
    /// </summary>
    public sealed class NanoMeasurementCycle
    {
        private readonly NanoSensorReader _sensorReader;
        private int _sampleCount;
        private int _validValueCount;
        private int _failedReadCount;
        private double _temperatureSum;
        private int _temperatureCount;
        private readonly NanoRunningStatistics _temperatureStats = new NanoRunningStatistics();
        private double _humiditySum;
        private int _humidityCount;
        private readonly NanoRunningStatistics _humidityStats = new NanoRunningStatistics();
        private double _pressureSum;
        private int _pressureCount;
        private readonly NanoRunningStatistics _pressureStats = new NanoRunningStatistics();
        private double _pm1Sum;
        private int _pm1Count;
        private double _pm25Sum;
        private int _pm25Count;
        private readonly NanoRunningStatistics _pm25Stats = new NanoRunningStatistics();
        private double _pm4Sum;
        private int _pm4Count;
        private double _pm10Sum;
        private int _pm10Count;
        private readonly NanoRunningStatistics _pm10Stats = new NanoRunningStatistics();
        private double _numberPm05Sum;
        private int _numberPm05Count;
        private double _numberPm1Sum;
        private int _numberPm1Count;
        private double _numberPm25Sum;
        private int _numberPm25Count;
        private double _numberPm4Sum;
        private int _numberPm4Count;
        private double _numberPm10Sum;
        private int _numberPm10Count;
        private double _typicalParticleSizeSum;
        private int _typicalParticleSizeCount;
        private int _sht40OkCount;
        private int _sht40ErrorCount;
        private int _bmp280OkCount;
        private int _bmp280ErrorCount;
        private int _sps30OkCount;
        private int _sps30WaitCount;
        private int _sps30ErrorCount;
        private int _sps30BatterySkippedCount;
        private MeasurementStatus _status;
        private DateTime _intervalStartedAt;
        private NanoSensorReadSet _lastReadSet;

        /// <summary>
        /// Creates a measurement cycle over the configured sensor reader.
        /// </summary>
        public NanoMeasurementCycle(NanoSensorReader sensorReader)
        {
            _sensorReader = sensorReader;
            Reset(AlignIntervalStart(DateTime.UtcNow, 10));
        }

        // Number of samples collected in the currently open interval.
        public int CurrentSampleCount
        {
            get
            {
                return _sampleCount;
            }
        }

        public DateTime IntervalStartedAt
        {
            get
            {
                return _intervalStartedAt;
            }
        }

        // Last raw sensor read set captured by the current cycle.
        public NanoSensorReadSet LastReadSet
        {
            get
            {
                return _lastReadSet;
            }
        }

        /// <summary>
        /// Reads one sample, updates aggregation and returns a final record when the interval is complete.
        /// </summary>
        public bool TryRunMeasurementStep(StationConfiguration configuration, string stationId, NanoPowerState powerState, out NanoMeasurementRecord record)
        {
            EnsureAlignedOpenInterval(configuration, DateTime.UtcNow);
            bool batteryActive = powerState != null && powerState.BatteryActive;
            NanoSensorReadSet sample = _sensorReader.ReadCurrentSample(batteryActive);
            _lastReadSet = sample;
            AddSample(sample, powerState);
            int expectedSamples = GetExpectedSamples(configuration);
            DateTime intervalEndsAt = GetIntervalEndsAt(configuration);
            if (ShouldLogSampleProgress(_sampleCount, expectedSamples))
            {
                Debug.WriteLine("Vzorek " + _sampleCount + "/" + expectedSamples + ": SHT40=" + State(sample.Sht40) + ", BMP280=" + State(sample.Bmp280) + ", SPS30=" + State(sample.Sps30));
                Debug.WriteLine("Sensor values: " + Values(sample));
            }

            if (sample.SampledAt < intervalEndsAt)
            {
                record = null;
                return false;
            }

            record = CloseInterval(stationId, intervalEndsAt, expectedSamples);
            Reset(intervalEndsAt);
            return true;
        }

        /// <summary>
        /// Calculates how many samples fit into one configured measurement interval.
        /// </summary>
        public static int GetExpectedSamples(StationConfiguration configuration)
        {
            int intervalMinutes = configuration.MeasurementIntervalMinutes < 1 ? 10 : configuration.MeasurementIntervalMinutes;
            int sampleSeconds = configuration.SamplePeriodSeconds < 1 ? 1 : configuration.SamplePeriodSeconds;
            int expected = (intervalMinutes * 60) / sampleSeconds;
            return expected < 1 ? 1 : expected;
        }

        public DateTime GetIntervalEndsAt(StationConfiguration configuration)
        {
            int intervalMinutes = configuration.MeasurementIntervalMinutes < 1 ? 10 : configuration.MeasurementIntervalMinutes;
            return _intervalStartedAt.AddMinutes(intervalMinutes);
        }

        private void EnsureAlignedOpenInterval(StationConfiguration configuration, DateTime now)
        {
            int intervalMinutes = configuration.MeasurementIntervalMinutes < 1 ? 10 : configuration.MeasurementIntervalMinutes;
            DateTime alignedStart = AlignIntervalStart(now, intervalMinutes);
            DateTime alignedEnd = alignedStart.AddMinutes(intervalMinutes);

            if (_sampleCount == 0)
            {
                if (_intervalStartedAt != alignedStart)
                {
                    _intervalStartedAt = alignedStart;
                    Debug.WriteLine("Interval zarovnan na casovou hranici od " + _intervalStartedAt + " do " + alignedEnd + ".");
                }

                return;
            }

            DateTime currentEnd = _intervalStartedAt.AddMinutes(intervalMinutes);
            if (now < _intervalStartedAt || now >= currentEnd.AddMinutes(intervalMinutes))
            {
                Debug.WriteLine("Cas se posunul mimo otevreny interval, zacinam nove zarovnane okno od " + alignedStart + " do " + alignedEnd + ".");
                Reset(alignedStart);
            }
        }

        private void AddSample(NanoSensorReadSet sample, NanoPowerState powerState)
        {
            _sampleCount++;

            if (powerState != null && powerState.BatteryActive)
            {
                AddStatus(MeasurementStatus.BackupPowerActive);
            }

            AddSht40(sample.Sht40);
            AddBmp280(sample.Bmp280);
            AddSps30(sample.Sps30);
        }

        private void AddSht40(Sht40ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null && result.Value.CrcOk)
            {
                _temperatureSum += result.Value.TemperatureCelsius;
                _temperatureCount++;
                _temperatureStats.Add(result.Value.TemperatureCelsius);
                _humiditySum += result.Value.RelativeHumidityPercent;
                _humidityCount++;
                _humidityStats.Add(result.Value.RelativeHumidityPercent);
                _validValueCount += 2;
                _sht40OkCount++;
                return;
            }

            _failedReadCount++;
            _sht40ErrorCount++;
            AddStatus(MeasurementStatus.Sht40ReadFailed);
        }

        private void AddBmp280(Bmp280ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null)
            {
                _pressureSum += result.Value.PressureHpa;
                _pressureCount++;
                _pressureStats.Add(result.Value.PressureHpa);
                _validValueCount++;
                _bmp280OkCount++;
                return;
            }

            _failedReadCount++;
            _bmp280ErrorCount++;
            AddStatus(MeasurementStatus.Bmp280ReadFailed);
        }

        private void AddSps30(Sps30ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null)
            {
                _pm1Sum += result.Value.MassPm1;
                _pm1Count++;
                _pm25Sum += result.Value.MassPm25;
                _pm25Count++;
                _pm25Stats.Add(result.Value.MassPm25);
                _pm4Sum += result.Value.MassPm4;
                _pm4Count++;
                _pm10Sum += result.Value.MassPm10;
                _pm10Count++;
                _pm10Stats.Add(result.Value.MassPm10);
                _numberPm05Sum += result.Value.NumberPm05;
                _numberPm05Count++;
                _numberPm1Sum += result.Value.NumberPm1;
                _numberPm1Count++;
                _numberPm25Sum += result.Value.NumberPm25;
                _numberPm25Count++;
                _numberPm4Sum += result.Value.NumberPm4;
                _numberPm4Count++;
                _numberPm10Sum += result.Value.NumberPm10;
                _numberPm10Count++;
                _typicalParticleSizeSum += result.Value.TypicalParticleSize;
                _typicalParticleSizeCount++;
                _validValueCount += 10;
                _sps30OkCount++;
                return;
            }

            if (result != null && result.ErrorCode == SensorErrorCode.Timeout)
            {
                _sps30WaitCount++;
                return;
            }

            _failedReadCount++;
            if (result != null && result.ErrorCode == SensorErrorCode.DisabledOnBattery)
            {
                _sps30BatterySkippedCount++;
            }
            else
            {
                _sps30ErrorCount++;
            }

            AddStatus(result != null && result.ErrorCode == SensorErrorCode.DisabledOnBattery
                ? MeasurementStatus.Sps30DisabledOnBattery
                : MeasurementStatus.Sps30ReadFailed);
        }

        private NanoMeasurementRecord CloseInterval(string stationId, DateTime endedAt, int expectedSamples)
        {
            NanoMeasurementRecord record = new NanoMeasurementRecord();
            record.StationId = stationId;
            record.SequenceNumber = NanoMeasurementSequence.FromUtc(endedAt);
            record.SampledAt = endedAt;
            record.TemperatureCelsius = Average(_temperatureSum, _temperatureCount);
            record.RelativeHumidityPercent = Average(_humiditySum, _humidityCount);
            record.PressureHpa = Average(_pressureSum, _pressureCount);
            record.Pm1 = Average(_pm1Sum, _pm1Count);
            record.Pm25 = Average(_pm25Sum, _pm25Count);
            record.Pm4 = Average(_pm4Sum, _pm4Count);
            record.Pm10 = Average(_pm10Sum, _pm10Count);
            record.NumberPm05 = Average(_numberPm05Sum, _numberPm05Count);
            record.NumberPm1 = Average(_numberPm1Sum, _numberPm1Count);
            record.NumberPm25 = Average(_numberPm25Sum, _numberPm25Count);
            record.NumberPm4 = Average(_numberPm4Sum, _numberPm4Count);
            record.NumberPm10 = Average(_numberPm10Sum, _numberPm10Count);
            record.TypicalParticleSize = Average(_typicalParticleSizeSum, _typicalParticleSizeCount);
            record.ValidValueCount = _validValueCount;
            record.FailedReadCount = _failedReadCount;
            record.ExpectedSampleCount = expectedSamples;
            record.ReceivedSampleCount = _sampleCount;
            record.Sht40OkCount = _sht40OkCount;
            record.Sht40ErrorCount = _sht40ErrorCount;
            record.Bmp280OkCount = _bmp280OkCount;
            record.Bmp280ErrorCount = _bmp280ErrorCount;
            record.Sps30OkCount = _sps30OkCount;
            record.Sps30WaitCount = _sps30WaitCount;
            record.Sps30ErrorCount = _sps30ErrorCount;
            record.Sps30BatterySkippedCount = _sps30BatterySkippedCount;
            record.TemperatureMin = _temperatureStats.Minimum;
            record.TemperatureMax = _temperatureStats.Maximum;
            record.TemperatureStdDev = _temperatureStats.StandardDeviation();
            record.HumidityMin = _humidityStats.Minimum;
            record.HumidityMax = _humidityStats.Maximum;
            record.HumidityStdDev = _humidityStats.StandardDeviation();
            record.PressureMin = _pressureStats.Minimum;
            record.PressureMax = _pressureStats.Maximum;
            record.PressureStdDev = _pressureStats.StandardDeviation();
            record.Pm25Min = _pm25Stats.Minimum;
            record.Pm25Max = _pm25Stats.Maximum;
            record.Pm25StdDev = _pm25Stats.StandardDeviation();
            record.Pm10Min = _pm10Stats.Minimum;
            record.Pm10Max = _pm10Stats.Maximum;
            record.Pm10StdDev = _pm10Stats.StandardDeviation();
            record.Status = ResolveStatus(expectedSamples);
            Debug.WriteLine("Interval uzavren od " + _intervalStartedAt + " do " + endedAt + ", record #" + record.SequenceNumber + ", samples=" + _sampleCount + ", status=" + record.Status.ToString());
            return record;
        }

        private MeasurementStatus ResolveStatus(int expectedSamples)
        {
            if (_status != MeasurementStatus.Ok)
            {
                return _status;
            }

            if (_temperatureCount < expectedSamples / 2)
            {
                return MeasurementStatus.InsufficientSamples;
            }

            return MeasurementStatus.Ok;
        }

        private void AddStatus(MeasurementStatus status)
        {
            if (_status == MeasurementStatus.Ok)
            {
                _status = status;
            }
        }

        private void Reset(DateTime startedAt)
        {
            _sampleCount = 0;
            _validValueCount = 0;
            _failedReadCount = 0;
            _temperatureSum = 0;
            _temperatureCount = 0;
            _temperatureStats.Reset();
            _humiditySum = 0;
            _humidityCount = 0;
            _humidityStats.Reset();
            _pressureSum = 0;
            _pressureCount = 0;
            _pressureStats.Reset();
            _pm1Sum = 0;
            _pm1Count = 0;
            _pm25Sum = 0;
            _pm25Count = 0;
            _pm25Stats.Reset();
            _pm4Sum = 0;
            _pm4Count = 0;
            _pm10Sum = 0;
            _pm10Count = 0;
            _pm10Stats.Reset();
            _numberPm05Sum = 0;
            _numberPm05Count = 0;
            _numberPm1Sum = 0;
            _numberPm1Count = 0;
            _numberPm25Sum = 0;
            _numberPm25Count = 0;
            _numberPm4Sum = 0;
            _numberPm4Count = 0;
            _numberPm10Sum = 0;
            _numberPm10Count = 0;
            _typicalParticleSizeSum = 0;
            _typicalParticleSizeCount = 0;
            _sht40OkCount = 0;
            _sht40ErrorCount = 0;
            _bmp280OkCount = 0;
            _bmp280ErrorCount = 0;
            _sps30OkCount = 0;
            _sps30WaitCount = 0;
            _sps30ErrorCount = 0;
            _sps30BatterySkippedCount = 0;
            _status = MeasurementStatus.Ok;
            _intervalStartedAt = startedAt;
        }

        private static DateTime AlignIntervalStart(DateTime utcTime, int intervalMinutes)
        {
            if (intervalMinutes < 1)
            {
                intervalMinutes = 10;
            }

            long intervalTicks = TimeSpan.TicksPerMinute * intervalMinutes;
            long alignedTicks = (utcTime.Ticks / intervalTicks) * intervalTicks;
            return new DateTime(alignedTicks);
        }

        private static double Average(double sum, int count)
        {
            return count == 0 ? 0 : sum / count;
        }

        private static bool ShouldLogSampleProgress(int sampleCount, int expectedSamples)
        {
            if (sampleCount == 1 || sampleCount >= expectedSamples)
            {
                return true;
            }

            int progressStep = expectedSamples / 10;
            if (progressStep < 1)
            {
                progressStep = 1;
            }

            return sampleCount % progressStep == 0;
        }

        private static string State(Sht40ReadResult result)
        {
            return result != null && result.IsSuccess ? "OK" : "ERR";
        }

        private static string State(Bmp280ReadResult result)
        {
            return result != null && result.IsSuccess ? "OK" : "ERR";
        }

        private static string State(Sps30ReadResult result)
        {
            return result != null && result.IsSuccess ? "OK" : result != null && result.ErrorCode == SensorErrorCode.DisabledOnBattery ? "BAT" : result != null && result.ErrorCode == SensorErrorCode.Timeout ? "WAIT" : "ERR";
        }

        private static string Values(NanoSensorReadSet sample)
        {
            string sht40 = sample.Sht40 != null && sample.Sht40.IsSuccess && sample.Sht40.Value != null
                ? "SHT40 T=" + sample.Sht40.Value.TemperatureCelsius + " C, RH=" + sample.Sht40.Value.RelativeHumidityPercent + " %"
                : "SHT40 " + Error(sample.Sht40);
            string bmp280 = sample.Bmp280 != null && sample.Bmp280.IsSuccess && sample.Bmp280.Value != null
                ? "BMP280 P=" + sample.Bmp280.Value.PressureHpa + " hPa, Taux=" + sample.Bmp280.Value.AuxiliaryTemperatureCelsius + " C"
                : "BMP280 " + Error(sample.Bmp280);
            string sps30 = sample.Sps30 != null && sample.Sps30.IsSuccess && sample.Sps30.Value != null
                ? "SPS30 PM1=" + sample.Sps30.Value.MassPm1
                    + ", PM2.5=" + sample.Sps30.Value.MassPm25
                    + ", PM4=" + sample.Sps30.Value.MassPm4
                    + ", PM10=" + sample.Sps30.Value.MassPm10
                    + ", N0.5=" + sample.Sps30.Value.NumberPm05
                    + ", N1=" + sample.Sps30.Value.NumberPm1
                    + ", N2.5=" + sample.Sps30.Value.NumberPm25
                    + ", N4=" + sample.Sps30.Value.NumberPm4
                    + ", N10=" + sample.Sps30.Value.NumberPm10
                    + ", Size=" + sample.Sps30.Value.TypicalParticleSize
                : "SPS30 " + Error(sample.Sps30);
            return sht40 + " | " + bmp280 + " | " + sps30;
        }

        private static string Error(Sht40ReadResult result)
        {
            return result == null ? "ERR null" : "ERR " + result.ErrorCode.ToString() + " " + result.ErrorMessage;
        }

        private static string Error(Bmp280ReadResult result)
        {
            return result == null ? "ERR null" : "ERR " + result.ErrorCode.ToString() + " " + result.ErrorMessage;
        }

        private static string Error(Sps30ReadResult result)
        {
            if (result != null && result.ErrorCode == SensorErrorCode.Timeout)
            {
                return "WAIT " + result.ErrorMessage;
            }

            return result == null ? "ERR null" : "ERR " + result.ErrorCode.ToString() + " " + result.ErrorMessage;
        }
    }
}
