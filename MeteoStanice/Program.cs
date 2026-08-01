using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Application entry point; only composes dependencies and starts the coordinator.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Runs a minimal debug loop used to validate nanoFramework deployment.
        /// </summary>
        public static void Main()
        {
            IStationConfigurationStore configurationStore = new FlashStationConfigurationStore();
            StationConfigurationService configurationService = new StationConfigurationService(configurationStore);
            StationConfiguration configuration = configurationService.LoadOrCreateDefault();
            NanoProvisioningService provisioningService = new NanoProvisioningService(configurationStore, configurationService, new NanoFrameworkProvisioningPortal());
            configuration = provisioningService.RunStartupWindow(configuration);
            StationIdentityService identityService = new StationIdentityService();
            StationIdentity identity = identityService.CreateIdentity(configuration);

            Debug.WriteLine("Board: " + BoardProfile.BoardName);
            Debug.WriteLine("Station: " + identity.StationId + " / " + identity.DeviceHardwareId);
            Debug.WriteLine("Setup SSID: " + configuration.ProvisioningAccessPointName);
            new BootProvisioningWindow().PrintStartupWindow(configuration);

            BootSensorSmokeService sensorSmokeService = new BootSensorSmokeService();
            FakeNanoFriendlySensors sensors = new FakeNanoFriendlySensors();
            NanoMeasurementBuilder measurementBuilder = new NanoMeasurementBuilder();
            NanoMeasurementQueue measurementQueue = new NanoMeasurementQueue(8);
            NanoDeviceEventQueue eventQueue = new NanoDeviceEventQueue(8);
            NanoDeviceEventService eventService = new NanoDeviceEventService(eventQueue);
            NanoUploadClient uploadClient = new NanoUploadClient();
            NanoMeasurementUploadService uploadService = new NanoMeasurementUploadService(measurementQueue, uploadClient);
            NanoDeviceEventUploadService eventUploadService = new NanoDeviceEventUploadService(eventQueue, uploadClient);
            FakeNanoPowerMonitor powerMonitor = new FakeNanoPowerMonitor();
            NanoPowerMonitoringService powerService = new NanoPowerMonitoringService(powerMonitor, eventService);
            NanoWifiService wifiService = new NanoWifiService(eventService);
            NanoWifiHardwareDiagnostics wifiHardwareDiagnostics = new NanoWifiHardwareDiagnostics();
            NanoServerStatusService serverStatusService = new NanoServerStatusService(eventService);
            int counter = 0;

            eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.System, DeviceEventCode.DeviceStarted, "Program", "Firmware started.");
            NanoWifiStatus wifiStatus = wifiService.Connect(configuration, identity.StationId);
            Debug.WriteLine("Wi-Fi state=" + wifiStatus.State.ToString() + ", IP=" + wifiStatus.IpAddress);
            Debug.WriteLine("Real Wi-Fi target SSID=" + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
            wifiHardwareDiagnostics.PrintAdapterCount();
            wifiHardwareDiagnostics.ScanAndPrintNetworks();
            bool realWifiConnected = wifiHardwareDiagnostics.TryConnect(configuration);
            Debug.WriteLine("Real Wi-Fi connected=" + realWifiConnected);
            wifiHardwareDiagnostics.PrintAllNetworkInterfaces();
            NanoServerStatus serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
            Debug.WriteLine("Server state=" + serverStatus.State.ToString());

            // Keep the app alive and produce regular output for the debugger console.
            while (true)
            {
                BootSensorReadResult result = sensorSmokeService.Read();
                Debug.WriteLine("Hello world from XIAO ESP32C3 / nanoFramework: " + counter);
                if (result.IsSuccess)
                {
                    BootSensorSample sample = result.Value;
                    Debug.WriteLine("Smoke sample T=" + sample.TemperatureCelsius + " C, RH=" + sample.RelativeHumidityPercent + " %");
                }
                else
                {
                    Debug.WriteLine("Smoke sample failed: " + result.ErrorCode.ToString() + " " + result.ErrorMessage);
                }

                Sht40ReadResult sht40 = sensors.ReadSht40();
                Bmp280ReadResult bmp280 = sensors.ReadBmp280();
                Sps30ReadResult sps30 = sensors.ReadSps30();
                Debug.WriteLine("SHT40 T=" + sht40.Value.TemperatureCelsius + " C, RH=" + sht40.Value.RelativeHumidityPercent + " %");
                Debug.WriteLine("BMP280 P=" + bmp280.Value.PressureHpa + " hPa");
                Debug.WriteLine("SPS30 PM2.5=" + sps30.Value.MassPm25 + " ug/m3, PM10=" + sps30.Value.MassPm10 + " ug/m3");

                NanoMeasurementRecord record = measurementBuilder.Build(identity.StationId, sht40, bmp280, sps30);
                measurementQueue.Enqueue(record);
                Debug.WriteLine("Record #" + record.SequenceNumber + " values=" + record.ValidValueCount + ", failed=" + record.FailedReadCount + ", status=" + record.Status.ToString());
                Debug.WriteLine("Queue count=" + measurementQueue.Count + "/" + measurementQueue.Capacity);
                uploadService.TryUploadOne();
                Debug.WriteLine("Queue after upload=" + measurementQueue.Count + "/" + measurementQueue.Capacity);
                NanoPowerState powerState = powerService.ReadAndReport(identity.StationId);
                Debug.WriteLine("Power mains=" + powerState.MainsAvailable + ", battery=" + powerState.BatteryActive + ", voltage=" + powerState.BatteryVoltage);
                eventUploadService.TryUploadOne();
                eventUploadService.TryUploadOne();
                Debug.WriteLine("Event queue after upload=" + eventQueue.Count);

                counter++;
                Thread.Sleep(1000);
            }
        }
    }
}
