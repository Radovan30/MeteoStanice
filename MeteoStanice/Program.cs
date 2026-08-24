using System;
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
        private const int PowerTransitionStabilizationMilliseconds = 1200;
        private const int MeasurementQueueCapacity = 64;
        private const int EventQueueCapacity = 16;
        private const int UploadRetryDelayMilliseconds = 60000;
        private const int UploadSuccessDelayMilliseconds = 5000;

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
            NanoMeasurementQueue measurementQueue = new NanoMeasurementQueue(MeasurementQueueCapacity);
            NanoDeviceEventQueue eventQueue = new NanoDeviceEventQueue(EventQueueCapacity);
            NanoDeviceEventService eventService = new NanoDeviceEventService(eventQueue);
            NanoUploadClient uploadClient = new NanoUploadClient();
            uploadClient.ConfigureIdentity(identity);
            NanoMeasurementUploadService uploadService = new NanoMeasurementUploadService(measurementQueue, uploadClient);
            NanoDeviceEventUploadService eventUploadService = new NanoDeviceEventUploadService(eventQueue, uploadClient);
            NanoPowerMonitor powerMonitor = new NanoPowerMonitor();
            NanoPowerMonitoringService powerService = new NanoPowerMonitoringService(powerMonitor, eventService);
            NanoWifiHardwareDiagnostics wifiHardwareDiagnostics = new NanoWifiHardwareDiagnostics();
            NanoServerStatusService serverStatusService = new NanoServerStatusService(eventService);
            NanoClockService clockService = new NanoClockService();
            int counter = 0;
            bool hasLastBatteryActive = false;
            bool lastBatteryActive = false;
            long nextUploadAttemptTicks = 0;

            eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.System, DeviceEventCode.DeviceStarted, "Program", "Firmware started.");
            bool realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, 0);
            clockService.TrySynchronize(realWifiConnected);
            measurementCycle = new NanoMeasurementCycle(sensorReader);
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

            uploadClient.ConfigureHttp(configuration.ApiBaseUrl, configuration.ApiKey, serverStatus.State == NanoServerConnectionState.Available);
            PrintRuntimeValues(configuration, measurementQueue, eventQueue);

            // Keep the app alive and run one configured meteo sample step per loop.
            while (true)
            {
                if (HandleSetupRequest(buttons, display, wifiHardwareDiagnostics, provisioningService, configurationStore, configurationService, identityService, sensorReader, measurementQueue, eventQueue, clockService, serverStatusService, uploadClient, ref configuration, ref identity, ref measurementCycle, ref realWifiConnected, counter))
                {
                    counter++;
                    continue;
                }

                DateTime loopStartedAt = DateTime.UtcNow;
                NanoPowerState powerState = powerService.ReadAndReport(identity.StationId);
                if (hasLastBatteryActive && lastBatteryActive != powerState.BatteryActive)
                {
                    Debug.WriteLine("Power source changed, sensor status set to pending and one sample skipped.");
                    display.ShowSensorPending();
                    lastBatteryActive = powerState.BatteryActive;
                    if (SleepUntilSetupPressed(buttons, PowerTransitionStabilizationMilliseconds))
                    {
                        continue;
                    }

                    counter++;
                    continue;
                }

                hasLastBatteryActive = true;
                lastBatteryActive = powerState.BatteryActive;

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
                        Debug.WriteLine("Fronta mereni je plna, MeasurementRecord #" + record.SequenceNumber + " nelze ulozit. Zahozeno=" + measurementQueue.DroppedCount);
                        eventService.Report(identity.StationId, DeviceEventSeverity.Critical, DeviceEventCategory.Storage, DeviceEventCode.LocalQueueFull, "MeasurementQueue", "Lokalni fronta mereni je plna.");
                    }
                }
                display.ShowSensorStatus(measurementCycle.LastReadSet);

                if (HandleSetupRequest(buttons, display, wifiHardwareDiagnostics, provisioningService, configurationStore, configurationService, identityService, sensorReader, measurementQueue, eventQueue, clockService, serverStatusService, uploadClient, ref configuration, ref identity, ref measurementCycle, ref realWifiConnected, counter))
                {
                    counter++;
                    continue;
                }

                if (buttons.IsSetupPressed())
                {
                    continue;
                }

                int uploadedMeasurements = 0;
                int uploadedEvents = 0;
                if (CanAttemptScheduledUpload(ref nextUploadAttemptTicks))
                {
                    uploadedMeasurements = uploadService.TryUploadPending(1);
                    if (uploadedMeasurements == 0)
                    {
                        uploadedEvents = eventUploadService.TryUploadPending(1);
                    }

                    ScheduleNextUploadAttempt(ref nextUploadAttemptTicks, uploadClient.LastUploadSucceeded);
                }

                if (uploadedMeasurements > 0 || uploadedEvents > 0)
                {
                    Debug.WriteLine("Uploaded measurements=" + uploadedMeasurements + ", events=" + uploadedEvents);
                    Debug.WriteLine("Queues after upload: measurements=" + measurementQueue.Count + "/" + measurementQueue.Capacity + ", events=" + eventQueue.Count + "/" + eventQueue.Capacity);
                }
                displayIpAddress = realWifiConnected ? wifiHardwareDiagnostics.GetWirelessIpAddress() : "0.0.0.0";
                clockService.TrySynchronizeIfDue(realWifiConnected);
                display.ShowRuntime(counter, realWifiConnected, configuration.WifiSsid, displayIpAddress, realWifiConnected ? string.Empty : wifiHardwareDiagnostics.LastConnectionMessage, clockService.GetDisplayDate(), clockService.GetDisplayTime(), GetLocationDisplayText(configuration), powerState.BatteryActive, powerState.BatteryVoltage, powerState.HasBatteryVoltage, GetNextUploadTimeText(configuration, measurementCycle, clockService), GetUploadStatusText(uploadClient, configuration), GetQueueStatusText(measurementQueue));

                counter++;
                SleepUntilSetupPressed(buttons, GetRuntimeLoopDelayMilliseconds(configuration, loopStartedAt));
            }
        }

        private static bool CanAttemptScheduledUpload(ref long nextUploadAttemptTicks)
        {
            if (nextUploadAttemptTicks == 0)
            {
                return true;
            }

            if (DateTime.UtcNow.Ticks < nextUploadAttemptTicks)
            {
                return false;
            }

            nextUploadAttemptTicks = 0;
            return true;
        }

        private static void ScheduleNextUploadAttempt(ref long nextUploadAttemptTicks, bool lastUploadSucceeded)
        {
            int delayMilliseconds = lastUploadSucceeded ? UploadSuccessDelayMilliseconds : UploadRetryDelayMilliseconds;
            nextUploadAttemptTicks = DateTime.UtcNow.Ticks + (delayMilliseconds * TimeSpan.TicksPerMillisecond);
        }

        private static bool HandleSetupRequest(NanoTtgoButtons buttons, NanoTtgoDisplay display, NanoWifiHardwareDiagnostics wifiHardwareDiagnostics, NanoProvisioningService provisioningService, IStationConfigurationStore configurationStore, StationConfigurationService configurationService, StationIdentityService identityService, NanoSensorReader sensorReader, NanoMeasurementQueue measurementQueue, NanoDeviceEventQueue eventQueue, NanoClockService clockService, NanoServerStatusService serverStatusService, NanoUploadClient uploadClient, ref StationConfiguration configuration, ref StationIdentity identity, ref NanoMeasurementCycle measurementCycle, ref bool realWifiConnected, int counter)
        {
            if (!buttons.ConsumeSetupRequest())
            {
                return false;
            }

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
                uploadClient.ConfigureIdentity(identity);
                measurementCycle = new NanoMeasurementCycle(sensorReader);
                PrintRuntimeValues(configuration, measurementQueue, eventQueue);
                Debug.WriteLine("Konfigurace po setupu znovu nactena z flash, SSID=" + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
                realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                clockService.TrySynchronize(realWifiConnected);
                measurementCycle = new NanoMeasurementCycle(sensorReader);
                NanoServerStatus serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
                uploadClient.ConfigureHttp(configuration.ApiBaseUrl, configuration.ApiKey, serverStatus.State == NanoServerConnectionState.Available);
            }
            else
            {
                Debug.WriteLine("Setup skoncil bez nove ulozene konfigurace, znovu pripojuji stavajici Wi-Fi.");
                configuration = configurationService.LoadOrCreateDefault();
                identity = identityService.CreateIdentity(configuration);
                uploadClient.ConfigureIdentity(identity);
                measurementCycle = new NanoMeasurementCycle(sensorReader);
                PrintRuntimeValues(configuration, measurementQueue, eventQueue);
                realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                clockService.TrySynchronize(realWifiConnected);
                measurementCycle = new NanoMeasurementCycle(sensorReader);
                NanoServerStatus serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
                uploadClient.ConfigureHttp(configuration.ApiBaseUrl, configuration.ApiKey, serverStatus.State == NanoServerConnectionState.Available);
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

            return true;
        }

        private static bool SleepUntilSetupPressed(NanoTtgoButtons buttons, int milliseconds)
        {
            int elapsed = 0;
            while (elapsed < milliseconds)
            {
                if (buttons.IsSetupPressed())
                {
                    return true;
                }

                int delay = milliseconds - elapsed;
                if (delay > 25)
                {
                    delay = 25;
                }

                Thread.Sleep(delay);
                elapsed += delay;
            }

            return false;
        }

        private static int GetRuntimeLoopDelayMilliseconds(StationConfiguration configuration, DateTime loopStartedAt)
        {
            int sampleSeconds = configuration.SamplePeriodSeconds < 1 ? 1 : configuration.SamplePeriodSeconds;
            int targetMilliseconds = sampleSeconds * 1000;
            long elapsedTicks = DateTime.UtcNow.Ticks - loopStartedAt.Ticks;
            int elapsedMilliseconds = elapsedTicks <= 0 ? 0 : (int)(elapsedTicks / 10000);
            int remainingMilliseconds = targetMilliseconds - elapsedMilliseconds;
            return remainingMilliseconds < 1 ? 1 : remainingMilliseconds;
        }

        private static string GetNextUploadTimeText(StationConfiguration configuration, NanoMeasurementCycle measurementCycle, NanoClockService clockService)
        {
            return clockService.GetDisplayTime(measurementCycle.GetIntervalEndsAt(configuration));
        }

        private static string GetUploadStatusText(NanoUploadClient uploadClient, StationConfiguration configuration)
        {
            string apiBaseUrl = StationConfigurationService.NormalizeApiBaseUrl(configuration == null ? string.Empty : configuration.ApiBaseUrl);
            if (apiBaseUrl.Length == 0)
            {
                return "API";
            }

            if (uploadClient != null && uploadClient.LastUploadAttempted)
            {
                if (uploadClient.LastHttpStatusCode > 0)
                {
                    return "H" + uploadClient.LastHttpStatusCode;
                }

                return uploadClient.LastUploadSucceeded ? "OK" : "ERR";
            }

            return "--";
        }

        private static string GetQueueStatusText(NanoMeasurementQueue measurementQueue)
        {
            if (measurementQueue == null)
            {
                return "Q--";
            }

            if (measurementQueue.DroppedCount > 0)
            {
                return "D" + TwoDigitLimit(measurementQueue.DroppedCount);
            }

            return "Q" + TwoDigitLimit(measurementQueue.Count);
        }

        private static string TwoDigitLimit(int value)
        {
            if (value < 0)
            {
                value = 0;
            }

            if (value > 99)
            {
                return "99";
            }

            return value < 10 ? "0" + value : value.ToString();
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
