using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Describes simplified Wi-Fi connection state.
    /// </summary>
    public enum NanoWifiConnectionState
    {
        // Wi-Fi is not configured or not connected.
        Disconnected,
        // Wi-Fi connection is being attempted.
        Connecting,
        // Wi-Fi is connected.
        Connected,
        // Wi-Fi connection failed.
        Failed
    }

    /// <summary>
    /// Stores Wi-Fi state without using network libraries.
    /// </summary>
    public sealed class NanoWifiStatus
    {
        // Current connection state.
        public NanoWifiConnectionState State { get; set; }
        // Configured SSID.
        public string Ssid { get; set; }
        // Assigned or configured IP address.
        public string IpAddress { get; set; }
        // Short diagnostic message.
        public string Message { get; set; }
    }

    /// <summary>
    /// Simulates Wi-Fi connection decisions before real Wi-Fi APIs are enabled.
    /// </summary>
    public sealed class NanoWifiService
    {
        private readonly NanoDeviceEventService _eventService;

        /// <summary>
        /// Creates Wi-Fi service backed by device event reporting.
        /// </summary>
        public NanoWifiService(NanoDeviceEventService eventService)
        {
            _eventService = eventService;
        }

        /// <summary>
        /// Evaluates configured Wi-Fi and returns a simulated connection state.
        /// </summary>
        public NanoWifiStatus Connect(StationConfiguration configuration, string stationId)
        {
            NanoWifiStatus status = new NanoWifiStatus();
            status.Ssid = configuration.WifiSsid;

            if (configuration.WifiSsid == null || configuration.WifiSsid.Length == 0)
            {
                status.State = NanoWifiConnectionState.Disconnected;
                status.IpAddress = "0.0.0.0";
                status.Message = "Wi-Fi SSID neni nastaveno.";
                _eventService.ClearActive();
                _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Network, DeviceEventCode.WifiConnectionFailed, "WiFi", status.Message);
                Debug.WriteLine("Wi-Fi: " + status.Message);
                return status;
            }

            status.State = NanoWifiConnectionState.Connected;
            status.IpAddress = configuration.UseDhcp ? "DHCP" : configuration.StaticIpAddress;
            status.Message = "Wi-Fi konfigurace je pripravena.";
            Debug.WriteLine("Wi-Fi: " + status.Message + " SSID=" + status.Ssid + " IP=" + status.IpAddress);
            return status;
        }
    }
}
