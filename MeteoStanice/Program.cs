using System;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Application entry point; only composes dependencies and starts the coordinator.
    /// </summary>
    public class Program
    {
        private const int SetupButtonTransitionBlockMilliseconds = 1500;
        private const int UploadWorkerPauseWaitMilliseconds = 70000;
        private const int EventQueueCapacity = 16;
        private const int RawSampleQueueCapacity = 60;
        private const int PersistentRawSampleQueueCapacity = 4320;
        private const int RawSamplePeriodSeconds = 10;

        /// <summary>
        /// Composes services and runs the ten-second sampling loop.
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
            display.SetStationIdentity(identity.StationId, identity.StationName);
            display.ShowLoading("CONFIG");
            NanoTtgoButtons buttons = new NanoTtgoButtons();
            buttons.Initialize();
            display.ShowLoading("BUTTON");

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
            NanoDeviceEventQueue eventQueue = new NanoDeviceEventQueue(EventQueueCapacity);
            NanoRawSampleQueue rawSampleQueue = new NanoRawSampleQueue(RawSampleQueueCapacity);
            NanoRawSamplePersistentQueue persistentRawSampleQueue = new NanoRawSamplePersistentQueue(PersistentRawSampleQueueCapacity);
            NanoRawSampleBuilder rawSampleBuilder = new NanoRawSampleBuilder();
            NanoDeviceEventService eventService = new NanoDeviceEventService(eventQueue);
            NanoUploadClient uploadClient = new NanoUploadClient();
            uploadClient.ConfigureIdentity(identity);
            uploadClient.ConfigureConfiguration(configurationStore, configuration);
            uploadClient.ConfigureBoot(Guid.NewGuid().ToString());
            NanoDeviceEventUploadService eventUploadService = new NanoDeviceEventUploadService(eventQueue, uploadClient);
            NanoRawSampleUploadService rawSampleUploadService = new NanoRawSampleUploadService(rawSampleQueue, persistentRawSampleQueue, uploadClient);
            NanoPowerMonitor powerMonitor = new NanoPowerMonitor();
            NanoPowerMonitoringService powerService = new NanoPowerMonitoringService(powerMonitor, eventService);
            NanoWifiHardwareDiagnostics wifiHardwareDiagnostics = new NanoWifiHardwareDiagnostics();
            NanoServerStatusService serverStatusService = new NanoServerStatusService(eventService);
            NanoClockService clockService = new NanoClockService();
            NanoWifiSupervisor wifiSupervisor = new NanoWifiSupervisor(wifiHardwareDiagnostics);
            NanoSensorEventTracker sensorEventTracker = new NanoSensorEventTracker(eventService);
            int counter = 0;
            bool clockSyncReported = false;
            NanoUploadWorker uploadWorker = new NanoUploadWorker(eventQueue, rawSampleQueue, persistentRawSampleQueue, eventUploadService, rawSampleUploadService, uploadClient, wifiSupervisor);

            eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.System, DeviceEventCode.DeviceStarted, "Program", "Firmware started.");
            bool realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, 0);
            if (!realWifiConnected && configuration.WifiSsid != null && configuration.WifiSsid.Length > 0)
            {
                eventService.Report(identity.StationId, DeviceEventSeverity.Warning, DeviceEventCategory.Network, DeviceEventCode.WifiConnectionFailed, "WiFi", "Wi-Fi pri startu nepripojeno: " + wifiHardwareDiagnostics.LastConnectionMessage);
            }

            wifiSupervisor.Configure(configuration, realWifiConnected);
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

            uploadClient.ConfigureHttp(configuration.ApiBaseUrl, configuration.ApiKey, serverStatus.State == NanoServerConnectionState.Available);
            uploadWorker.Start();
            PrintRuntimeValues(configuration, rawSampleQueue, persistentRawSampleQueue, eventQueue);

            // Keep the app alive and run one ten-second sample step per loop.
            while (true)
            {
                display.SetStationIdentity(identity.StationId, identity.StationName);
                bool wifiNow = wifiSupervisor.IsConnected;
                if (wifiNow != realWifiConnected)
                {
                    realWifiConnected = wifiNow;
                    eventService.ClearActive();
                    if (realWifiConnected)
                    {
                        eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.Network, DeviceEventCode.WifiConnected, "WiFi", "Wi-Fi znovu pripojeno.");
                        clockService.TrySynchronize(true);
                    }
                    else
                    {
                        eventService.Report(identity.StationId, DeviceEventSeverity.Warning, DeviceEventCategory.Network, DeviceEventCode.WifiConnectionFailed, "WiFi", "Wi-Fi spojeni ztraceno.");
                    }
                }

                if (!clockSyncReported && clockService.HasEverSynchronized)
                {
                    clockSyncReported = true;
                    eventService.Report(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.Time, DeviceEventCode.ClockSynchronized, "Clock", "Cas synchronizovan z NTP, vzorkovani aktivni.");
                }

                if (HandleSetupRequest(buttons, display, wifiHardwareDiagnostics, provisioningService, configurationStore, configurationService, identityService, rawSampleQueue, persistentRawSampleQueue, eventQueue, clockService, serverStatusService, uploadClient, uploadWorker, wifiSupervisor, eventService, ref configuration, ref identity, ref realWifiConnected, counter))
                {
                    counter++;
                    continue;
                }

                DateTime loopStartedAt = DateTime.UtcNow;
                // The power state (battery active, low battery, voltage) is only attached to the sample
                // and reported as an event; sampling continues unchanged because the ADC battery
                // detection is not reliable (low cell voltage at night while still on the cable).
                NanoPowerState powerState = powerService.ReadAndReport(identity.StationId);
                NanoSensorReadSet readSet = sensorReader.ReadCurrentSample();

                // Without a synchronized clock the sample would carry a 1970 timestamp, collide with
                // other unsynchronized samples and be silently dropped as a duplicate by the API.
                bool timeValid = clockService.HasValidTime();
                if (timeValid)
                {
                    sensorEventTracker.Track(identity.StationId, readSet);
                }
                else if (counter % 6 == 0)
                {
                    Debug.WriteLine("Cas neni synchronizovan z NTP, vzorek se neuklada.");
                }

                NanoRawSample rawSample;
                if (timeValid && rawSampleBuilder.TryBuild(readSet, powerState, out rawSample))
                {
                    if (rawSampleQueue.Enqueue(rawSample))
                    {
                        Debug.WriteLine("Raw sample #" + rawSample.SequenceNumber + " ulozen do fronty " + rawSampleQueue.Count + "/" + rawSampleQueue.Capacity + ".");
                    }
                    else
                    {
                        Debug.WriteLine("Fronta raw samples je plna, sample #" + rawSample.SequenceNumber + " zahozen. Zahozeno=" + rawSampleQueue.DroppedCount);
                    }
                }
                display.ShowSensorStatus(readSet);

                if (HandleSetupRequest(buttons, display, wifiHardwareDiagnostics, provisioningService, configurationStore, configurationService, identityService, rawSampleQueue, persistentRawSampleQueue, eventQueue, clockService, serverStatusService, uploadClient, uploadWorker, wifiSupervisor, eventService, ref configuration, ref identity, ref realWifiConnected, counter))
                {
                    counter++;
                    continue;
                }

                if (buttons.IsSetupPressed())
                {
                    continue;
                }

                displayIpAddress = realWifiConnected ? wifiHardwareDiagnostics.GetWirelessIpAddress() : "0.0.0.0";
                clockService.TrySynchronizeIfDue(realWifiConnected);
                display.ShowRuntime(counter, realWifiConnected, configuration.WifiSsid, displayIpAddress, realWifiConnected ? string.Empty : wifiHardwareDiagnostics.LastConnectionMessage, clockService.GetDisplayDate(), clockService.GetDisplayTime(), GetLocationDisplayText(configuration), powerState.BatteryActive, powerState.BatteryVoltage, powerState.HasBatteryVoltage, GetNextUploadTimeText(clockService), GetUploadStatusText(uploadClient, configuration), clockService.HasValidTime() ? GetQueueStatusText(rawSampleQueue, persistentRawSampleQueue) : "T--");

                counter++;
                SleepUntilSetupPressed(buttons, GetRuntimeLoopDelayMilliseconds(configuration, loopStartedAt));
            }
        }

        private static bool HandleSetupRequest(NanoTtgoButtons buttons, NanoTtgoDisplay display, NanoWifiHardwareDiagnostics wifiHardwareDiagnostics, NanoProvisioningService provisioningService, IStationConfigurationStore configurationStore, StationConfigurationService configurationService, StationIdentityService identityService, NanoRawSampleQueue rawSampleQueue, NanoRawSamplePersistentQueue persistentRawSampleQueue, NanoDeviceEventQueue eventQueue, NanoClockService clockService, NanoServerStatusService serverStatusService, NanoUploadClient uploadClient, NanoUploadWorker uploadWorker, NanoWifiSupervisor wifiSupervisor, NanoDeviceEventService eventService, ref StationConfiguration configuration, ref StationIdentity identity, ref bool realWifiConnected, int counter)
        {
            if (!buttons.ConsumeSetupRequest())
            {
                return false;
            }

            Debug.WriteLine("Setup tlacitko stisknuto za behu, spoustim setup AP bez restartu.");
            // Setup mode stops measuring; the events explain the resulting gap in the data.
            eventService.ReportTransition(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.Configuration, DeviceEventCode.SetupModeEntered, "Setup", "Setup rezim spusten tlacitkem, mereni preruseno.");
            uploadWorker.Pause(UploadWorkerPauseWaitMilliseconds);
            display.ShowSetup(configuration.ProvisioningAccessPointName, configuration.ProvisioningAddress);
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
                uploadClient.ConfigureConfiguration(configurationStore, configuration);
                PrintRuntimeValues(configuration, rawSampleQueue, persistentRawSampleQueue, eventQueue);
                Debug.WriteLine("Konfigurace po setupu znovu nactena z flash, SSID=" + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
                realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                wifiSupervisor.Configure(configuration, realWifiConnected);
                clockService.TrySynchronize(realWifiConnected);
                NanoServerStatus serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
                uploadClient.ConfigureHttp(configuration.ApiBaseUrl, configuration.ApiKey, serverStatus.State == NanoServerConnectionState.Available);
                uploadWorker.Resume();
            }
            else
            {
                Debug.WriteLine("Setup skoncil bez nove ulozene konfigurace, znovu pripojuji stavajici Wi-Fi.");
                configuration = configurationService.LoadOrCreateDefault();
                identity = identityService.CreateIdentity(configuration);
                uploadClient.ConfigureIdentity(identity);
                uploadClient.ConfigureConfiguration(configurationStore, configuration);
                PrintRuntimeValues(configuration, rawSampleQueue, persistentRawSampleQueue, eventQueue);
                realWifiConnected = ConnectConfiguredWifi(display, wifiHardwareDiagnostics, configuration, counter);
                wifiSupervisor.Configure(configuration, realWifiConnected);
                clockService.TrySynchronize(realWifiConnected);
                NanoServerStatus serverStatus = serverStatusService.Check(configuration, realWifiConnected, identity.StationId);
                uploadClient.ConfigureHttp(configuration.ApiBaseUrl, configuration.ApiKey, serverStatus.State == NanoServerConnectionState.Available);
                uploadWorker.Resume();
            }

            if (provisioningResult.ConfigurationSaved)
            {
                eventService.ReportTransition(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.Configuration, DeviceEventCode.ConfigurationApplied, "Setup", "Nova konfigurace ulozena a pouzita.");
            }

            eventService.ReportTransition(identity.StationId, DeviceEventSeverity.Information, DeviceEventCategory.Configuration, DeviceEventCode.SetupModeExited, "Setup", "Setup rezim ukoncen, mereni pokracuje.");

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
            long sampleTicks = TimeSpan.TicksPerSecond * RawSamplePeriodSeconds;
            long nowTicks = DateTime.UtcNow.Ticks;
            long ticksIntoPeriod = nowTicks % sampleTicks;
            long remainingTicks = ticksIntoPeriod == 0 ? sampleTicks : sampleTicks - ticksIntoPeriod;
            int remainingMilliseconds = (int)(remainingTicks / 10000);
            return remainingMilliseconds < 1 ? 1 : remainingMilliseconds;
        }

        private static string GetNextUploadTimeText(NanoClockService clockService)
        {
            long periodTicks = TimeSpan.TicksPerSecond * RawSamplePeriodSeconds;
            long nowTicks = DateTime.UtcNow.Ticks;
            long nextTicks = nowTicks - (nowTicks % periodTicks) + periodTicks;
            return clockService.GetDisplayTime(new DateTime(nextTicks));
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

        private static string GetQueueStatusText(NanoRawSampleQueue rawSampleQueue, NanoRawSamplePersistentQueue persistentRawSampleQueue)
        {
            if (rawSampleQueue == null || persistentRawSampleQueue == null)
            {
                return "Q--";
            }

            int dropped = rawSampleQueue.DroppedCount + persistentRawSampleQueue.DroppedCount;
            if (dropped > 0)
            {
                return "D" + TwoDigitLimit(dropped);
            }

            return "Q" + TwoDigitLimit(rawSampleQueue.Count + persistentRawSampleQueue.Count);
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

        private static void PrintRuntimeValues(StationConfiguration configuration, NanoRawSampleQueue rawSampleQueue, NanoRawSamplePersistentQueue persistentRawSampleQueue, NanoDeviceEventQueue eventQueue)
        {
            Debug.WriteLine("Aktualni hodnoty nastaveni:");
            Debug.WriteLine("  Stanice ID: " + configuration.StationId);
            Debug.WriteLine("  GUID: " + configuration.StationGuid);
            Debug.WriteLine("  Nazev: " + configuration.StationName);
            Debug.WriteLine("  Misto: " + (configuration.LocationText == null || configuration.LocationText.Length == 0 ? "<nenastaveno>" : configuration.LocationText));
            Debug.WriteLine("  Wi-Fi SSID: " + (configuration.WifiSsid == null || configuration.WifiSsid.Length == 0 ? "<nenastaveno>" : configuration.WifiSsid));
            Debug.WriteLine("  IP rezim: " + (configuration.UseDhcp ? "DHCP" : configuration.StaticIpAddress));
            Debug.WriteLine("  API URL: " + (configuration.ApiBaseUrl == null || configuration.ApiBaseUrl.Length == 0 ? "<nenastaveno>" : configuration.ApiBaseUrl));
            Debug.WriteLine("  Perioda raw vzorku a uploadu: " + RawSamplePeriodSeconds + " s.");
            Debug.WriteLine("  Fronta raw RAM: " + rawSampleQueue.Count + "/" + rawSampleQueue.Capacity);
            Debug.WriteLine("  Fronta raw flash: " + persistentRawSampleQueue.Count + "/" + persistentRawSampleQueue.Capacity);
            Debug.WriteLine("  Fronta udalosti flash: " + eventQueue.Count + "/" + eventQueue.Capacity + ", zahozeno=" + eventQueue.DroppedCount);
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
            if (configuration.WifiSsid != null && configuration.WifiSsid.Length > 0)
            {
                wifiHardwareDiagnostics.PrintAdapterCount();
            }
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
