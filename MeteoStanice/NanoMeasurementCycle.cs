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
        private readonly FakeNanoFriendlySensors _sensors;

        /// <summary>
        /// Creates a sensor reader over the currently available nano-safe sensor provider.
        /// </summary>
        public NanoSensorReader(FakeNanoFriendlySensors sensors)
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
        private int _sequenceNumber;
        private int _sampleCount;
        private int _validValueCount;
        private int _failedReadCount;
        private double _temperatureSum;
        private int _temperatureCount;
        private double _humiditySum;
        private int _humidityCount;
        private double _pressureSum;
        private int _pressureCount;
        private double _pm25Sum;
        private int _pm25Count;
        private double _pm10Sum;
        private int _pm10Count;
        private MeasurementStatus _status;
        private DateTime _intervalStartedAt;

        /// <summary>
        /// Creates a measurement cycle over the configured sensor reader.
        /// </summary>
        public NanoMeasurementCycle(NanoSensorReader sensorReader)
        {
            _sensorReader = sensorReader;
            Reset(DateTime.UtcNow);
        }

        // Number of samples collected in the currently open interval.
        public int CurrentSampleCount
        {
            get
            {
                return _sampleCount;
            }
        }

        /// <summary>
        /// Reads one sample, updates aggregation and returns a final record when the interval is complete.
        /// </summary>
        public bool TryRunMeasurementStep(StationConfiguration configuration, string stationId, NanoPowerState powerState, out NanoMeasurementRecord record)
        {
            bool batteryActive = powerState != null && powerState.BatteryActive;
            NanoSensorReadSet sample = _sensorReader.ReadCurrentSample(batteryActive);
            AddSample(sample, powerState);
            int expectedSamples = GetExpectedSamples(configuration);
            if (ShouldLogSampleProgress(_sampleCount, expectedSamples))
            {
                Debug.WriteLine("Vzorek " + _sampleCount + "/" + expectedSamples + ": SHT40=" + State(sample.Sht40) + ", BMP280=" + State(sample.Bmp280) + ", SPS30=" + State(sample.Sps30));
            }

            if (_sampleCount < expectedSamples)
            {
                record = null;
                return false;
            }

            record = CloseInterval(stationId, sample.SampledAt, expectedSamples);
            Reset(sample.SampledAt);
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
                _humiditySum += result.Value.RelativeHumidityPercent;
                _humidityCount++;
                _validValueCount += 2;
                return;
            }

            _failedReadCount++;
            AddStatus(MeasurementStatus.Sht40ReadFailed);
        }

        private void AddBmp280(Bmp280ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null)
            {
                _pressureSum += result.Value.PressureHpa;
                _pressureCount++;
                _validValueCount++;
                return;
            }

            _failedReadCount++;
            AddStatus(MeasurementStatus.Bmp280ReadFailed);
        }

        private void AddSps30(Sps30ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null)
            {
                _pm25Sum += result.Value.MassPm25;
                _pm25Count++;
                _pm10Sum += result.Value.MassPm10;
                _pm10Count++;
                _validValueCount += 2;
                return;
            }

            _failedReadCount++;
            AddStatus(result != null && result.ErrorCode == SensorErrorCode.DisabledOnBattery
                ? MeasurementStatus.Sps30DisabledOnBattery
                : MeasurementStatus.Sps30ReadFailed);
        }

        private NanoMeasurementRecord CloseInterval(string stationId, DateTime endedAt, int expectedSamples)
        {
            _sequenceNumber++;

            NanoMeasurementRecord record = new NanoMeasurementRecord();
            record.StationId = stationId;
            record.SequenceNumber = _sequenceNumber;
            record.SampledAt = endedAt;
            record.TemperatureCelsius = Average(_temperatureSum, _temperatureCount);
            record.RelativeHumidityPercent = Average(_humiditySum, _humidityCount);
            record.PressureHpa = Average(_pressureSum, _pressureCount);
            record.Pm25 = Average(_pm25Sum, _pm25Count);
            record.Pm10 = Average(_pm10Sum, _pm10Count);
            record.ValidValueCount = _validValueCount;
            record.FailedReadCount = _failedReadCount;
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
            _humiditySum = 0;
            _humidityCount = 0;
            _pressureSum = 0;
            _pressureCount = 0;
            _pm25Sum = 0;
            _pm25Count = 0;
            _pm10Sum = 0;
            _pm10Count = 0;
            _status = MeasurementStatus.Ok;
            _intervalStartedAt = startedAt;
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
            return result != null && result.IsSuccess ? "OK" : result != null && result.ErrorCode == SensorErrorCode.DisabledOnBattery ? "BAT" : "ERR";
        }
    }
}
