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
        private const int SetupButtonTransitionBlockMilliseconds = 1500;

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

            INanoFriendlySensors sensors = new NanoRealSensorProvider();
            NanoSensorReader sensorReader = new NanoSensorReader(sensors);
            NanoMeasurementCycle measurementCycle = new NanoMeasurementCycle(sensorReader);
            NanoMeasurementQueue measurementQueue = new NanoMeasurementQueue(8);
            NanoDeviceEventQueue eventQueue = new NanoDeviceEventQueue(8);
            NanoDeviceEventService eventService = new NanoDeviceEventService(eventQueue);
            NanoUploadClient uploadClient = new NanoUploadClient();
            NanoMeasurementUploadService uploadService = new NanoMeasurementUploadService(measurementQueue, uploadClient);
            NanoDeviceEventUploadService eventUploadService = new NanoDeviceEventUploadService(eventQueue, uploadClient);
            NanoPowerMonitor powerMonitor = new NanoPowerMonitor();
            NanoPowerMonitoringService powerService = new NanoPowerMonitoringService(powerMonitor, eventService);
            NanoWifiHardwareDiagnostics wifiHardwareDiagnostics = new NanoWifiHardwareDiagnostics();
            NanoServerStatusService serverStatusService = new NanoServerStatusService(eventService);
            NanoClockService clockService = new NanoClockService();
            int counter = 0;

            eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.System, DeviceEventCode.DeviceStarted, "Program", "Firmware started.");
            bool realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, 0);
            clockService.TrySynchronize(realWifiConnected);
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
            PrintRuntimeValues(configuration, measurementQueue, eventQueue);

            // Keep the app alive and run one configured meteo sample step per loop.
            while (true)
            {
                NanoPowerState powerState = powerService.ReadAndReport(identity.StationId);
                NanoMeasurementRecord record;
                if (measurementCycle.TryRunMeasurementStep(configuration, identity.StationId, powerState, out record))
                {
                    Debug.WriteLine("Power mains=" + powerState.MainsAvailable + ", battery=" + powerState.BatteryActive + ", voltage=" + powerState.BatteryVoltage);
                    if (measurementQueue.Enqueue(record))
                    {
                        Debug.WriteLine("Finalni MeasurementRecord #" + record.SequenceNumber + " ulozen do fronty, values=" + record.ValidValueCount + ", failed=" + record.FailedReadCount + ", status=" + record.Status.ToString());
                    }
                    else
                    {
                        Debug.WriteLine("Fronta mereni je plna, MeasurementRecord #" + record.SequenceNumber + " nelze ulozit.");
                        eventService.Report(identity.StationId, DeviceEventSeverity.Critical, DeviceEventCategory.Storage, DeviceEventCode.LocalQueueFull, "MeasurementQueue", "Lokalni fronta mereni je plna.");
                    }
                }
                display.ShowSensorStatus(measurementCycle.LastReadSet);

                int uploadedMeasurements = uploadService.TryUploadPending(configuration.MeasurementUploadsPerCycle);
                int uploadedEvents = eventUploadService.TryUploadPending(configuration.EventUploadsPerCycle);
                if (uploadedMeasurements > 0 || uploadedEvents > 0)
                {
                    Debug.WriteLine("Uploaded measurements=" + uploadedMeasurements + ", events=" + uploadedEvents);
                    Debug.WriteLine("Queues after upload: measurements=" + measurementQueue.Count + "/" + measurementQueue.Capacity + ", events=" + eventQueue.Count + "/" + eventQueue.Capacity);
                }
                displayIpAddress = realWifiConnected ? wifiHardwareDiagnostics.GetWirelessIpAddress() : "0.0.0.0";
                clockService.TrySynchronizeIfDue(realWifiConnected);
                display.ShowRuntime(counter, realWifiConnected, configuration.WifiSsid, displayIpAddress, realWifiConnected ? string.Empty : wifiHardwareDiagnostics.LastConnectionMessage, clockService.GetDisplayDate(), clockService.GetDisplayTime(), GetLocationDisplayText(configuration), powerState.BatteryActive, powerState.BatteryVoltage, powerState.HasBatteryVoltage);

                if (buttons.ConsumeSetupRequest())
                {
                    Debug.WriteLine("Setup tlacitko stisknuto za behu, spoustim setup AP bez restartu.");
                    display.ShowSetup(configuration.ProvisioningAccessPointName, configuration.ProvisioningHostName);
                    wifiHardwareDiagnostics.DisconnectClient();
                    buttons.IgnoreCurrentPressUntilReleased();
                    buttons.BlockSetupRequestsFor(SetupButtonTransitionBlockMilliseconds);
                    buttons.WaitUntilReleased(SetupButtonTransitionBlockMilliseconds);
                    NanoProvisioningResult provisioningResult = provisioningService.RunUntilSavedOrCancelled(configuration, buttons);
                    buttons.BlockSetupRequestsFor(SetupButtonTransitionBlockMilliseconds);
                    configuration = provisioningResult.Configuration;
                    if (provisioningResult.ConfigurationSaved)
                    {
                        configuration = configurationService.LoadOrCreateDefault();
                        identity = identityService.CreateIdentity(configuration);
                        measurementCycle = new NanoMeasurementCycle(sensorReader);
                        PrintRuntimeValues(configuration, measurementQueue, eventQueue);
                        Debug.WriteLine("Konfigurace po setupu znovu nactena z flash, SSID=" + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
                        realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                        clockService.TrySynchronize(realWifiConnected);
                        serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
                        uploadClient.ConfigureHttp(configuration.ApiBaseUrl, serverStatus.State == NanoServerConnectionState.Available);
                    }
                    else
                    {
                        Debug.WriteLine("Setup skoncil bez nove ulozene konfigurace, znovu pripojuji stavajici Wi-Fi.");
                        configuration = configurationService.LoadOrCreateDefault();
                        identity = identityService.CreateIdentity(configuration);
                        measurementCycle = new NanoMeasurementCycle(sensorReader);
                        PrintRuntimeValues(configuration, measurementQueue, eventQueue);
                        realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                        clockService.TrySynchronize(realWifiConnected);
                        serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
                        uploadClient.ConfigureHttp(configuration.ApiBaseUrl, serverStatus.State == NanoServerConnectionState.Available);
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
                Thread.Sleep(GetRuntimeLoopDelayMilliseconds(configuration));
            }
        }

        private static int GetRuntimeLoopDelayMilliseconds(StationConfiguration configuration)
        {
            int sampleSeconds = configuration.SamplePeriodSeconds < 1 ? 1 : configuration.SamplePeriodSeconds;
            return sampleSeconds * 1000;
        }

        private static void PrintRuntimeValues(StationConfiguration configuration, NanoMeasurementQueue measurementQueue, NanoDeviceEventQueue eventQueue)
        {
            Debug.WriteLine("Aktualni hodnoty nastaveni:");
            Debug.WriteLine("  Stanice ID: " + configuration.StationId);
            Debug.WriteLine("  GUID: " + configuration.StationGuid);
            Debug.WriteLine("  Nazev: " + configuration.StationName);
            Debug.WriteLine("  Misto: " + (configuration.LocationText == null || configuration.LocationText.Length == 0 ? "<nenastaveno>" : configuration.LocationText));
            Debug.WriteLine("  Wi-Fi SSID: " + (configuration.WifiSsid == null || configuration.WifiSsid.Length == 0 ? "<nenastaveno>" : configuration.WifiSsid));
            Debug.WriteLine("  IP rezim: " + (configuration.UseDhcp ? "DHCP" : configuration.StaticIpAddress));
            Debug.WriteLine("  API URL: " + (configuration.ApiBaseUrl == null || configuration.ApiBaseUrl.Length == 0 ? "<nenastaveno>" : configuration.ApiBaseUrl));
            Debug.WriteLine("  Interval mereni: " + configuration.MeasurementIntervalMinutes + " min.");
            Debug.WriteLine("  Perioda vzorku: " + configuration.SamplePeriodSeconds + " s.");
            Debug.WriteLine("  Ocekavane vzorky v intervalu: " + NanoMeasurementCycle.GetExpectedSamples(configuration));
            Debug.WriteLine("  Upload mereni/cyklus: " + configuration.MeasurementUploadsPerCycle);
            Debug.WriteLine("  Upload udalosti/cyklus: " + configuration.EventUploadsPerCycle);
            Debug.WriteLine("  Fronta mereni: " + measurementQueue.Count + "/" + measurementQueue.Capacity);
            Debug.WriteLine("  Fronta udalosti: " + eventQueue.Count + "/" + eventQueue.Capacity);
        }

        private static string GetLocationDisplayText(StationConfiguration configuration)
        {
            if (configuration.LocationText == null || configuration.LocationText.Length == 0)
            {
                return "-";
            }

            return configuration.LocationText;
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
