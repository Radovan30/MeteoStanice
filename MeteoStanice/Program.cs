using System.Diagnostics;
using System.Threading;
using nanoFramework.Runtime.Native;

namespace MeteoStanice
{
    /// <summary>
    /// Application entry point; only composes dependencies and starts the coordinator.
    /// </summary>
    public class Program
    {
        private const int RuntimeLoopDelayMilliseconds = 1000;
        private const int MeasurementUploadsPerCycle = 2;
        private const int EventUploadsPerCycle = 4;

        /// <summary>
        /// Runs a minimal debug loop used to validate nanoFramework deployment.
        /// </summary>
        public static void Main()
        {
            IStationConfigurationStore configurationStore = new FlashStationConfigurationStore();
            StationConfigurationService configurationService = new StationConfigurationService(configurationStore);
            StationConfiguration configuration = configurationService.LoadOrCreateDefault();
            NanoProvisioningService provisioningService = new NanoProvisioningService(configurationStore, configurationService, new NanoFrameworkProvisioningPortal(configurationStore, configurationService));
            StationIdentityService identityService = new StationIdentityService();
            StationIdentity identity = identityService.CreateIdentity(configuration);
            NanoTtgoDisplay display = new NanoTtgoDisplay();
            display.Initialize();
            display.ShowLoading("CONFIG");
            NanoTtgoButtons buttons = new NanoTtgoButtons();
            buttons.Initialize();
            display.ShowLoading("BUTTON");

            if (configuration.ProvisioningRequestedOnNextBoot)
            {
                Debug.WriteLine("Mazani stareho setup flagu, setup se nespousti po restartu.");
                configuration.ProvisioningRequestedOnNextBoot = false;
                configurationStore.Save(configuration);
            }

            Debug.WriteLine("Stanice startuje primo v meteo rezimu, setup se spousti jen tlacitkem za behu.");
            display.ShowLoading("AP OFF");
            NanoFrameworkProvisioningPortal.DisableSavedAccessPoint();

            Debug.WriteLine("Board: " + BoardProfile.BoardName);
            Debug.WriteLine("Station: " + identity.StationId + " / " + identity.DeviceHardwareId);
            Debug.WriteLine("Station GUID: " + identity.StationGuid);
            Debug.WriteLine("Setup SSID: " + configuration.ProvisioningAccessPointName);
            new BootProvisioningWindow().PrintStartupWindow(configuration);
            display.ShowLoading("SERVICES");

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
            NanoWifiHardwareDiagnostics wifiHardwareDiagnostics = new NanoWifiHardwareDiagnostics();
            NanoServerStatusService serverStatusService = new NanoServerStatusService(eventService);
            int counter = 0;

            eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.System, DeviceEventCode.DeviceStarted, "Program", "Firmware started.");
            bool realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, 0);
            string displayIpAddress = wifiHardwareDiagnostics.GetWirelessIpAddress();
            if (configuration.ApiBaseUrl != null && configuration.ApiBaseUrl.Length > 0)
            {
                display.ShowLoading("SERVER");
            }

            NanoServerStatus serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
            Debug.WriteLine("Server state=" + serverStatus.State.ToString() + ", HTTP=" + serverStatus.HttpStatusCode);
            if (serverStatus.State == NanoServerConnectionState.NotConfigured)
            {
                Debug.WriteLine("API server zatim neni nastaven, pro test Wi-Fi je to v poradku.");
            }

            uploadClient.ConfigureHttp(configuration.ApiBaseUrl, serverStatus.State == NanoServerConnectionState.Available);

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
                int uploadedMeasurements = uploadService.TryUploadPending(MeasurementUploadsPerCycle);
                Debug.WriteLine("Queue after upload=" + measurementQueue.Count + "/" + measurementQueue.Capacity);
                NanoPowerState powerState = powerService.ReadAndReport(identity.StationId);
                Debug.WriteLine("Power mains=" + powerState.MainsAvailable + ", battery=" + powerState.BatteryActive + ", voltage=" + powerState.BatteryVoltage);
                int uploadedEvents = eventUploadService.TryUploadPending(EventUploadsPerCycle);
                Debug.WriteLine("Uploaded measurements=" + uploadedMeasurements + ", events=" + uploadedEvents);
                Debug.WriteLine("Event queue after upload=" + eventQueue.Count);
                displayIpAddress = realWifiConnected ? wifiHardwareDiagnostics.GetWirelessIpAddress() : "0.0.0.0";
                display.ShowRuntime(counter, realWifiConnected, configuration.WifiSsid, displayIpAddress, realWifiConnected ? string.Empty : wifiHardwareDiagnostics.LastConnectionMessage);

                if (buttons.ConsumeSetupRequest())
                {
                    Debug.WriteLine("Setup tlacitko stisknuto za behu, spoustim setup AP bez restartu.");
                    display.ShowSetup(configuration.ProvisioningAccessPointName, configuration.ProvisioningHostName);
                    wifiHardwareDiagnostics.DisconnectClient();
                    buttons.IgnoreCurrentPressUntilReleased();
                    NanoProvisioningResult provisioningResult = provisioningService.RunUntilSavedOrCancelled(configuration, buttons);
                    configuration = provisioningResult.Configuration;
                    if (provisioningResult.ConfigurationSaved)
                    {
                        configuration = configurationService.LoadOrCreateDefault();
                        identity = identityService.CreateIdentity(configuration);
                        Debug.WriteLine("Konfigurace po setupu znovu nactena z flash, SSID=" + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
                        realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                    }
                    else
                    {
                        Debug.WriteLine("Setup skoncil bez nove ulozene konfigurace, znovu pripojuji stavajici Wi-Fi.");
                        configuration = configurationService.LoadOrCreateDefault();
                        identity = identityService.CreateIdentity(configuration);
                        realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                    }

                    if (provisioningResult.RebootRequired)
                    {
                        Debug.WriteLine("Runtime provisioning nelze spustit bez restartu, restartuji jako fallback.");
                        display.ShowReboot();
                        configuration.ProvisioningRequestedOnNextBoot = false;
                        configurationStore.Save(configuration);
                        Thread.Sleep(250);
                        Power.RebootDevice(RebootOption.NormalReboot);
                    }

                    configuration.ProvisioningRequestedOnNextBoot = false;
                    configurationStore.Save(configuration);

                    if (provisioningResult.CancelledByButton)
                    {
                        Debug.WriteLine("Setup ukoncen tlacitkem, stanice pokracuje v meteo rezimu bez restartu.");
                    }
                }

                counter++;
                Thread.Sleep(RuntimeLoopDelayMilliseconds);
            }
        }

        private static bool ConnectConfiguredWifi(NanoTtgoDisplay display, NanoWifiHardwareDiagnostics wifiHardwareDiagnostics, StationConfiguration configuration, int counter)
        {
            display.ShowWifiConnecting(configuration.WifiSsid);
            display.ShowRuntime(counter, false, configuration.WifiSsid, "0.0.0.0", "WIFI START");
            Debug.WriteLine("Real Wi-Fi target SSID=" + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
            wifiHardwareDiagnostics.PrintAdapterCount();
            bool realWifiConnected = wifiHardwareDiagnostics.TryConnect(configuration, delegate (string message)
            {
                display.ShowRuntime(counter, false, configuration.WifiSsid, wifiHardwareDiagnostics.GetWirelessIpAddress(), message);
            });
            Debug.WriteLine("Real Wi-Fi connected=" + realWifiConnected + ", message=" + wifiHardwareDiagnostics.LastConnectionMessage);
            wifiHardwareDiagnostics.PrintAllNetworkInterfaces();
            string displayIpAddress = wifiHardwareDiagnostics.GetWirelessIpAddress();
            display.ShowRuntime(counter, realWifiConnected, configuration.WifiSsid, displayIpAddress, realWifiConnected ? string.Empty : wifiHardwareDiagnostics.LastConnectionMessage);
            return realWifiConnected;
        }
    }
}
