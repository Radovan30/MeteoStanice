using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Runs simple smoke checks for fake firmware components.
    /// </summary>
    public sealed class TestScenarioRunner
    {
        /// <summary>
        /// Exercises representative success, failure and recovery scenarios.
        /// </summary>
        public void RunSmokeChecks()
        {
            // One shared fake clock keeps scenario output deterministic.
            FakeTimeProvider timeProvider = new FakeTimeProvider();

            // Verify normal SHT40 read and CRC failure mode.
            FakeSht40Sensor sht40 = new FakeSht40Sensor(timeProvider);
            sht40.Initialize();
            Debug.WriteLine("Test SHT40 OK: " + sht40.Read().IsSuccess);
            sht40.ForceCrcFailure = true;
            Debug.WriteLine("Test SHT40 CRC: " + (sht40.Read().ErrorCode == SensorErrorCode.CrcFailed));

            // Verify missing BMP280 during initialization.
            FakeBmp280Sensor bmp280 = new FakeBmp280Sensor(timeProvider);
            bmp280.ForceNotDetected = true;
            Debug.WriteLine("Test BMP280 nedostupny: " + (bmp280.Initialize() == SensorInitializationResult.NotDetected));

            // Verify SPS30 fan error and battery-disabled mode.
            FakeSps30Sensor sps30 = new FakeSps30Sensor(timeProvider);
            sps30.Initialize();
            sps30.ForceFanError = true;
            Debug.WriteLine("Test SPS30 ventilator: " + (sps30.Read().ErrorCode == SensorErrorCode.FanError));
            sps30.ForceFanError = false;
            sps30.SetEnabled(false);
            Debug.WriteLine("Test SPS30 baterie: " + (sps30.Read().ErrorCode == SensorErrorCode.DisabledOnBattery));

            // Verify backup power state.
            FakePowerMonitor powerMonitor = new FakePowerMonitor(timeProvider);
            powerMonitor.MainsAvailable = false;
            Debug.WriteLine("Test napajeni baterie: " + powerMonitor.Read().BatteryActive);

            // Verify queue-full behavior.
            InMemoryPersistentQueue<MeasurementRecord> queue = new InMemoryPersistentQueue<MeasurementRecord>(1);
            Debug.WriteLine("Test fronta plna: " + (queue.Enqueue(new MeasurementRecord()) && !queue.Enqueue(new MeasurementRecord())));

            // Verify unavailable Wi-Fi or server upload path.
            FakeApiClient apiClient = new FakeApiClient();
            apiClient.WifiAvailable = false;
            Debug.WriteLine("Test Wi-Fi nedostupna: " + !apiClient.UploadMeasurement(new MeasurementRecord()));
        }
    }
}
