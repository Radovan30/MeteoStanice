using System;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Coordinates sensor reads, aggregation, local queueing and upload attempts.
    /// </summary>
    public sealed class MeasurementCoordinator
    {
        private readonly StationConfiguration _configuration;
        private readonly ITimeProvider _timeProvider;
        private readonly ISensor<Sht40Sample> _sht40;
        private readonly ISensor<Bmp280Sample> _bmp280;
        private readonly ISensor<Sps30Sample> _sps30;
        private readonly PowerMonitoringService _powerMonitoringService;
        private readonly MeasurementAggregationService _aggregationService;
        private readonly IPersistentQueue<MeasurementRecord> _measurementQueue;
        private readonly EventReportingService _eventReportingService;
        private readonly MeasurementUploadService _uploadService;
        private int _sampleCount;
        private long _measurementSequence;
        private bool _sht40Failed;
        private bool _bmp280Failed;
        private bool _sps30Failed;

        /// <summary>
        /// Creates the main measurement coordinator.
        /// </summary>
        public MeasurementCoordinator(
            StationConfiguration configuration,
            ITimeProvider timeProvider,
            ISensor<Sht40Sample> sht40,
            ISensor<Bmp280Sample> bmp280,
            ISensor<Sps30Sample> sps30,
            IPowerMonitor powerMonitor,
            MeasurementAggregationService aggregationService,
            IPersistentQueue<MeasurementRecord> measurementQueue,
            EventReportingService eventReportingService,
            MeasurementUploadService uploadService)
        {
            _configuration = configuration;
            _timeProvider = timeProvider;
            _sht40 = sht40;
            _bmp280 = bmp280;
            _sps30 = sps30;
            _powerMonitoringService = new PowerMonitoringService(powerMonitor);
            _aggregationService = aggregationService;
            _measurementQueue = measurementQueue;
            _eventReportingService = eventReportingService;
            _uploadService = uploadService;
        }

        /// <summary>
        /// Starts the infinite firmware loop.
        /// </summary>
        public void Run()
        {
            InitializeSensors();

            DateTime intervalStart = _timeProvider.GetNow().Value;
            _aggregationService.Reset(intervalStart);

            _eventReportingService.Report(
                DeviceEventSeverity.Information,
                DeviceEventCategory.System,
                DeviceEventCode.DeviceStarted,
                "Station",
                0,
                "Firmware stanice byl spusten.");

            while (true)
            {
                ExecuteOneCycle();
                Thread.Sleep(_configuration.SamplePeriodMilliseconds);
            }
        }

        private void InitializeSensors()
        {
            // Each sensor initializes independently so one failed sensor does not stop boot.
            if (_sht40.Initialize() != SensorInitializationResult.Success)
            {
                _eventReportingService.Report(DeviceEventSeverity.Error, DeviceEventCategory.Sensor, DeviceEventCode.Sht40NotDetected, "SHT40", 0, "SHT40 nebyl detekovan.");
            }

            if (_bmp280.Initialize() != SensorInitializationResult.Success)
            {
                _eventReportingService.Report(DeviceEventSeverity.Error, DeviceEventCategory.Sensor, DeviceEventCode.Bmp280NotDetected, "BMP280", 0, "BMP280 nebyl detekovan.");
            }

            if (_sps30.Initialize() != SensorInitializationResult.Success)
            {
                _eventReportingService.Report(DeviceEventSeverity.Error, DeviceEventCategory.Sensor, DeviceEventCode.Sps30NotDetected, "SPS30", 0, "SPS30 nebyl detekovan.");
            }
        }

        private void ExecuteOneCycle()
        {
            // Power state decides whether battery-only limitations apply in this cycle.
            PowerState powerState = _powerMonitoringService.ReadDebounced();
            if (powerState.BatteryActive)
            {
                _aggregationService.AddStatus(MeasurementStatus.BackupPowerActive);
            }

            // Environmental sensors are always sampled once per cycle.
            SensorReadResult<Sht40Sample> sht40Result = _sht40.Read();
            HandleSht40Result(sht40Result);
            _aggregationService.AddSht40(sht40Result);

            SensorReadResult<Bmp280Sample> bmp280Result = _bmp280.Read();
            HandleBmp280Result(bmp280Result);
            _aggregationService.AddBmp280(bmp280Result);

            SensorReadResult<Sps30Sample> sps30Result;
            // SPS30 is skipped on battery when configuration forbids running the fan.
            if (powerState.BatteryActive && !_configuration.Sps30EnabledOnBattery)
            {
                sps30Result = SensorReadResult<Sps30Sample>.Failure(SensorErrorCode.DisabledOnBattery, 0, "SPS30 je vypnuty pri napajeni z baterie.");
                _eventReportingService.Report(DeviceEventSeverity.Information, DeviceEventCategory.Power, DeviceEventCode.Sps30DisabledOnBattery, "SPS30", 0, sps30Result.ErrorMessage);
            }
            else
            {
                sps30Result = _sps30.Read();
            }

            HandleSps30Result(sps30Result);
            _aggregationService.AddSps30(sps30Result);

            // One completed cycle contributes to the current aggregation interval.
            _sampleCount++;
            Debug.WriteLine("Vzorek #" + _sampleCount + ": SHT40=" + sht40Result.IsSuccess + ", BMP280=" + bmp280Result.IsSuccess + ", SPS30=" + sps30Result.IsSuccess);

            // Closing the interval creates a durable record ready for upload.
            if (_sampleCount >= _configuration.ExpectedSamplesPerInterval)
            {
                CloseInterval();
            }

            // Upload is attempted opportunistically; failed items remain in queues.
            _uploadService.TryUploadPending();
        }

        private void CloseInterval()
        {
            // Sequence number is local and monotonic for records created by this boot.
            _measurementSequence++;
            DateTime now = _timeProvider.GetNow().Value;
            MeasurementRecord record = _aggregationService.CloseInterval(
                _configuration.StationId,
                _measurementSequence,
                now,
                _timeProvider.IsSynchronized());

            // Records are removed from the queue only after the upload service succeeds.
            if (!_measurementQueue.Enqueue(record))
            {
                _eventReportingService.Report(DeviceEventSeverity.Critical, DeviceEventCategory.Storage, DeviceEventCode.LocalQueueFull, "MeasurementQueue", 0, "Lokalni fronta mereni je plna.");
            }
            else
            {
                Debug.WriteLine("Ulozen desetiminutovy zaznam " + record.RecordId + " se stavem " + record.Status.ToString());
            }

            // Reset statistics after the record is safely queued or the queue-full event is reported.
            _sampleCount = 0;
            _aggregationService.Reset(now);
        }

        private void HandleSht40Result(SensorReadResult<Sht40Sample> result)
        {
            if (!result.IsSuccess)
            {
                // Active error state prevents repeated recovery events without a success first.
                _sht40Failed = true;
                DeviceEventCode code = result.ErrorCode == SensorErrorCode.CrcFailed ? DeviceEventCode.Sht40CrcFailed : DeviceEventCode.Sht40ReadFailed;
                _eventReportingService.Report(DeviceEventSeverity.Error, DeviceEventCategory.Sensor, code, "SHT40", result.RawErrorCode, result.ErrorMessage);
            }
            else if (_sht40Failed)
            {
                // Recovery is emitted once when the sensor starts responding again.
                _sht40Failed = false;
                _eventReportingService.ReportRecovery(DeviceEventCode.Sht40Recovered, "SHT40", "SHT40 znovu odpovida.");
            }
        }

        private void HandleBmp280Result(SensorReadResult<Bmp280Sample> result)
        {
            if (!result.IsSuccess)
            {
                // BMP280 failures are tracked separately from other sensor failures.
                _bmp280Failed = true;
                _eventReportingService.Report(DeviceEventSeverity.Error, DeviceEventCategory.Sensor, DeviceEventCode.Bmp280ReadFailed, "BMP280", result.RawErrorCode, result.ErrorMessage);
            }
            else if (_bmp280Failed)
            {
                // A single recovery event closes the previous failure period.
                _bmp280Failed = false;
                _eventReportingService.ReportRecovery(DeviceEventCode.Bmp280Recovered, "BMP280", "BMP280 znovu odpovida.");
            }
        }

        private void HandleSps30Result(SensorReadResult<Sps30Sample> result)
        {
            if (!result.IsSuccess)
            {
                // SPS30 diagnostics are mapped to stable server-side event codes.
                _sps30Failed = true;
                DeviceEventCode code = DeviceEventCode.Sps30ReadFailed;
                if (result.ErrorCode == SensorErrorCode.FanError)
                {
                    code = DeviceEventCode.Sps30FanError;
                }
                else if (result.ErrorCode == SensorErrorCode.LaserError)
                {
                    code = DeviceEventCode.Sps30LaserError;
                }
                else if (result.ErrorCode == SensorErrorCode.DisabledOnBattery)
                {
                    code = DeviceEventCode.Sps30DisabledOnBattery;
                }

                _eventReportingService.Report(DeviceEventSeverity.Error, DeviceEventCategory.Sensor, code, "SPS30", result.RawErrorCode, result.ErrorMessage);
            }
            else if (_sps30Failed)
            {
                _sps30Failed = false;
                _eventReportingService.ReportRecovery(DeviceEventCode.Sps30Restored, "SPS30", "SPS30 znovu odpovida.");
            }
        }
    }
}
