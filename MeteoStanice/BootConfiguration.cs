using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Contains the small boot-safe configuration model used before full firmware is restored.
    /// </summary>
    public sealed class StationConfiguration
    {
        // User-facing station identifier used in the web application.
        public string StationId { get; set; }
        // Immutable hardware identifier for the physical board.
        public string DeviceHardwareId { get; set; }
        // Human-friendly station name.
        public string StationName { get; set; }
        // Firmware version currently running on the station.
        public string FirmwareVersion { get; set; }
        // Wi-Fi network name used in meteo mode.
        public string WifiSsid { get; set; }
        // Wi-Fi password used in meteo mode.
        public string WifiPassword { get; set; }
        // True when IP address should be assigned by DHCP.
        public bool UseDhcp { get; set; }
        // Static IP address used when DHCP is disabled.
        public string StaticIpAddress { get; set; }
        // Gateway address used when DHCP is disabled.
        public string GatewayAddress { get; set; }
        // Subnet mask used when DHCP is disabled.
        public string SubnetMask { get; set; }
        // DNS server address used when DHCP is disabled.
        public string DnsAddress { get; set; }
        // Server API base URL.
        public string ApiBaseUrl { get; set; }
        // Name of the temporary setup access point.
        public string ProvisioningAccessPointName { get; set; }
        // Password for the temporary setup access point and setup form.
        public string ProvisioningPassword { get; set; }
        // Local web address used by the setup portal.
        public string ProvisioningAddress { get; set; }
        // Setup window length after boot.
        public int ProvisioningWindowMilliseconds { get; set; }
    }

    /// <summary>
    /// Describes an immutable station identity sent to the server.
    /// </summary>
    public sealed class StationIdentity
    {
        // User-facing station identifier.
        public string StationId { get; set; }
        // Immutable hardware identifier.
        public string DeviceHardwareId { get; set; }
        // Human-friendly station name.
        public string StationName { get; set; }
        // Firmware version currently running on the station.
        public string FirmwareVersion { get; set; }
    }

    /// <summary>
    /// Loads, validates and creates the boot-safe station configuration.
    /// </summary>
    public sealed class StationConfigurationService
    {
        // Default setup window length after boot.
        private const int DefaultProvisioningWindowMilliseconds = 60000;

        private readonly IStationConfigurationStore _configurationStore;

        /// <summary>
        /// Creates a configuration service.
        /// </summary>
        public StationConfigurationService(IStationConfigurationStore configurationStore)
        {
            _configurationStore = configurationStore;
        }

        /// <summary>
        /// Loads saved configuration or creates and saves defaults.
        /// </summary>
        public StationConfiguration LoadOrCreateDefault()
        {
            StationConfiguration configuration;
            if (_configurationStore.TryLoad(out configuration) && Validate(configuration))
            {
                if (configuration.ProvisioningWindowMilliseconds < DefaultProvisioningWindowMilliseconds)
                {
                    configuration.ProvisioningWindowMilliseconds = DefaultProvisioningWindowMilliseconds;
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Provisioning okno aktualizovano na 60000 ms.");
                }

                return configuration;
            }

            configuration = CreateDefault();
            _configurationStore.Save(configuration);
            return configuration;
        }

        /// <summary>
        /// Creates factory defaults used after first boot or invalid configuration.
        /// </summary>
        public StationConfiguration CreateDefault()
        {
            StationConfiguration configuration = new StationConfiguration();
            configuration.StationId = "METEO-TEST-001";
            configuration.DeviceHardwareId = BoardProfile.HardwareIdPrefix + "-UNKNOWN";
            configuration.StationName = "XIAO ESP32C3 meteostanice";
            configuration.FirmwareVersion = "0.1.0-test";
            configuration.WifiSsid = string.Empty;
            configuration.WifiPassword = string.Empty;
            configuration.UseDhcp = true;
            configuration.StaticIpAddress = "192.168.4.50";
            configuration.GatewayAddress = "192.168.4.1";
            configuration.SubnetMask = "255.255.255.0";
            configuration.DnsAddress = "8.8.8.8";
            configuration.ApiBaseUrl = string.Empty;
            configuration.ProvisioningAccessPointName = BoardProfile.ProvisioningAccessPointName;
            configuration.ProvisioningPassword = "12345678";
            configuration.ProvisioningAddress = "192.168.4.1";
            configuration.ProvisioningWindowMilliseconds = DefaultProvisioningWindowMilliseconds;
            return configuration;
        }

        /// <summary>
        /// Validates the minimum configuration needed to start firmware.
        /// </summary>
        public bool Validate(StationConfiguration configuration)
        {
            return configuration != null
                && configuration.StationId != null
                && configuration.DeviceHardwareId != null
                && configuration.StaticIpAddress != null
                && configuration.GatewayAddress != null
                && configuration.SubnetMask != null
                && configuration.DnsAddress != null
                && configuration.ProvisioningWindowMilliseconds > 0;
        }
    }

    /// <summary>
    /// Maintains station identity used for server registration and remote control.
    /// </summary>
    public sealed class StationIdentityService
    {
        /// <summary>
        /// Creates identity from station configuration.
        /// </summary>
        public StationIdentity CreateIdentity(StationConfiguration configuration)
        {
            StationIdentity identity = new StationIdentity();
            identity.StationId = configuration.StationId;
            identity.DeviceHardwareId = configuration.DeviceHardwareId;
            identity.StationName = configuration.StationName;
            identity.FirmwareVersion = configuration.FirmwareVersion;
            return identity;
        }
    }

    /// <summary>
    /// Opens a boot-safe simulated setup window.
    /// </summary>
    public sealed class BootProvisioningWindow
    {
        /// <summary>
        /// Writes setup details without starting Wi-Fi or web services.
        /// </summary>
        public void PrintStartupWindow(StationConfiguration configuration)
        {
            Debug.WriteLine("Provisioning okno: " + configuration.ProvisioningWindowMilliseconds + " ms.");
            Debug.WriteLine("Setup Wi-Fi SSID: " + configuration.ProvisioningAccessPointName);
            Debug.WriteLine("Setup heslo/PIN: " + configuration.ProvisioningPassword);
            Debug.WriteLine("Setup web: http://" + configuration.ProvisioningAddress + "/");
            Debug.WriteLine("Meteo Wi-Fi: " + (configuration.WifiSsid == string.Empty ? "<nenastaveno>" : configuration.WifiSsid));
            Debug.WriteLine("Meteo IP: " + (configuration.UseDhcp ? "DHCP" : configuration.StaticIpAddress));
        }
    }
}
