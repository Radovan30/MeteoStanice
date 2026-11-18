using System;
using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Keeps the station Wi-Fi client connected. Runs on the upload worker thread, so a blocking
    /// reconnect attempt never stops the ten-second sampling loop.
    /// </summary>
    public sealed class NanoWifiSupervisor
    {
        private const int FirstRetryDelaySeconds = 60;
        private const int MaximumRetryDelaySeconds = 600;
        // Upload attempts without any HTTP response before the link is considered lost.
        private const int NetworkFailuresBeforeReconnect = 3;

        private readonly NanoWifiHardwareDiagnostics _diagnostics;
        private readonly object _syncRoot = new object();
        private StationConfiguration _configuration;
        private bool _connected;
        private int _retryDelaySeconds = FirstRetryDelaySeconds;
        private long _nextAttemptTicks;
        private int _reconnectCount;

        public NanoWifiSupervisor(NanoWifiHardwareDiagnostics diagnostics)
        {
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// True while the Wi-Fi client is believed to be connected.
        /// </summary>
        public bool IsConnected
        {
            get { return _connected; }
        }

        /// <summary>
        /// Number of successful automatic reconnections since boot.
        /// </summary>
        public int ReconnectCount
        {
            get { return _reconnectCount; }
        }

        /// <summary>
        /// Sets the configuration and the result of a connection made by the main thread
        /// (boot or after setup mode). Call only while the upload worker is paused or not started.
        /// </summary>
        public void Configure(StationConfiguration configuration, bool connected)
        {
            lock (_syncRoot)
            {
                _configuration = configuration;
                _connected = connected;
                _retryDelaySeconds = FirstRetryDelaySeconds;
                _nextAttemptTicks = connected ? 0 : DateTime.UtcNow.Ticks + (FirstRetryDelaySeconds * TimeSpan.TicksPerSecond);
            }
        }

        /// <summary>
        /// Detects a lost connection (DHCP address lost or repeated upload attempts without a response)
        /// and reconnects with an exponential back-off from 1 to 10 minutes.
        /// Returns true when a reconnect attempt was made.
        /// </summary>
        public bool ReconnectIfNeeded(int consecutiveNetworkFailures)
        {
            lock (_syncRoot)
            {
                if (_configuration == null || _configuration.WifiSsid == null || _configuration.WifiSsid.Length == 0)
                {
                    return false;
                }

                if (_connected && consecutiveNetworkFailures == 0)
                {
                    _retryDelaySeconds = FirstRetryDelaySeconds;
                }

                bool addressLost = _configuration.UseDhcp && _diagnostics.GetWirelessIpAddress() == "0.0.0.0";
                bool linkSuspect = consecutiveNetworkFailures >= NetworkFailuresBeforeReconnect;
                if (_connected && !addressLost && !linkSuspect)
                {
                    return false;
                }

                if (_connected)
                {
                    Debug.WriteLine("Wi-Fi spojeni ztraceno (adresa=" + (addressLost ? "ztracena" : "ok") + ", selhani uploadu=" + consecutiveNetworkFailures + ").");
                    _connected = false;
                }

                long nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks < _nextAttemptTicks)
                {
                    return false;
                }

                Debug.WriteLine("Wi-Fi reconnect, dalsi pokus nejdrive za " + _retryDelaySeconds + " s.");
                bool connected = _diagnostics.TryConnect(_configuration);
                _nextAttemptTicks = DateTime.UtcNow.Ticks + (_retryDelaySeconds * TimeSpan.TicksPerSecond);
                _retryDelaySeconds = _retryDelaySeconds * 2 > MaximumRetryDelaySeconds ? MaximumRetryDelaySeconds : _retryDelaySeconds * 2;
                _connected = connected;
                if (connected)
                {
                    _reconnectCount++;
                    Debug.WriteLine("Wi-Fi znovu pripojeno, IP=" + _diagnostics.GetWirelessIpAddress());
                }
                else
                {
                    Debug.WriteLine("Wi-Fi reconnect selhal: " + _diagnostics.LastConnectionMessage);
                }

                return true;
            }
        }
    }

    /// <summary>
    /// Turns per-sample sensor results into debounced failure and recovery events.
    /// A failure is reported after three consecutive failed reads (30 s); SPS30 WAIT
    /// (data not ready or warm-up) is neutral and does not change the state.
    /// </summary>
    public sealed class NanoSensorEventTracker
    {
        private const int FailuresBeforeEvent = 3;

        private readonly NanoDeviceEventService _eventService;
        private readonly SensorEventState _sht40 = new SensorEventState();
        private readonly SensorEventState _bmp280 = new SensorEventState();
        private readonly SensorEventState _sps30 = new SensorEventState();

        public NanoSensorEventTracker(NanoDeviceEventService eventService)
        {
            _eventService = eventService;
        }

        public void Track(string stationId, NanoSensorReadSet readSet)
        {
            if (readSet == null)
            {
                return;
            }

            bool sht40Ok = readSet.Sht40 != null && readSet.Sht40.IsSuccess;
            SensorErrorCode sht40Error = readSet.Sht40 == null ? SensorErrorCode.NotDetected : readSet.Sht40.ErrorCode;
            TrackSensor(stationId, _sht40, "SHT40", sht40Ok, sht40Error, MapSht40(sht40Error), DeviceEventCode.Sht40Recovered);

            bool bmp280Ok = readSet.Bmp280 != null && readSet.Bmp280.IsSuccess;
            SensorErrorCode bmp280Error = readSet.Bmp280 == null ? SensorErrorCode.NotDetected : readSet.Bmp280.ErrorCode;
            TrackSensor(stationId, _bmp280, "BMP280", bmp280Ok, bmp280Error, bmp280Error == SensorErrorCode.NotDetected ? DeviceEventCode.Bmp280NotDetected : DeviceEventCode.Bmp280ReadFailed, DeviceEventCode.Bmp280Recovered);

            bool sps30Ok = readSet.Sps30 != null && readSet.Sps30.IsSuccess;
            SensorErrorCode sps30Error = readSet.Sps30 == null ? SensorErrorCode.NotDetected : readSet.Sps30.ErrorCode;
            if (!sps30Ok && sps30Error == SensorErrorCode.Timeout)
            {
                return;
            }

            TrackSensor(stationId, _sps30, "SPS30", sps30Ok, sps30Error, MapSps30(sps30Error), DeviceEventCode.Sps30Restored);
        }

        private void TrackSensor(string stationId, SensorEventState state, string component, bool ok, SensorErrorCode error, DeviceEventCode failureCode, DeviceEventCode recoveryCode)
        {
            if (ok)
            {
                state.ConsecutiveFailures = 0;
                if (state.Failed)
                {
                    state.Failed = false;
                    _eventService.ReportTransition(stationId, DeviceEventSeverity.Information, DeviceEventCategory.Sensor, recoveryCode, component, component + " opet meri.");
                }

                return;
            }

            state.ConsecutiveFailures++;
            if (!state.Failed && state.ConsecutiveFailures >= FailuresBeforeEvent)
            {
                state.Failed = true;
                _eventService.ReportTransition(stationId, DeviceEventSeverity.Error, DeviceEventCategory.Sensor, failureCode, component, component + " selhal: " + error.ToString());
            }
        }

        private static DeviceEventCode MapSht40(SensorErrorCode error)
        {
            if (error == SensorErrorCode.NotDetected) return DeviceEventCode.Sht40NotDetected;
            if (error == SensorErrorCode.CrcFailed) return DeviceEventCode.Sht40CrcFailed;
            return DeviceEventCode.Sht40ReadFailed;
        }

        private static DeviceEventCode MapSps30(SensorErrorCode error)
        {
            if (error == SensorErrorCode.NotDetected) return DeviceEventCode.Sps30NotDetected;
            if (error == SensorErrorCode.FanError) return DeviceEventCode.Sps30FanError;
            if (error == SensorErrorCode.LaserError) return DeviceEventCode.Sps30LaserError;
            return DeviceEventCode.Sps30ReadFailed;
        }

        private sealed class SensorEventState
        {
            public bool Failed { get; set; }
            public int ConsecutiveFailures { get; set; }
        }
    }
}
