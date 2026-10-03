using System;
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
        // Permanent station GUID generated once and stored in flash.
        public string StationGuid { get; set; }
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
        // Optional API key sent with station uploads.
        public string ApiKey { get; set; }
        // Length of one final measurement interval in minutes.
        public int MeasurementIntervalMinutes { get; set; }
        // Delay between individual sensor samples in seconds.
        public int SamplePeriodSeconds { get; set; }
        // Maximum measurement records uploaded during one runtime loop.
        public int MeasurementUploadsPerCycle { get; set; }
        // Maximum device events uploaded during one runtime loop.
        public int EventUploadsPerCycle { get; set; }
        // Human-entered station place name shown on the display.
        public string LocationText { get; set; }
        // Name of the temporary setup access point.
        public string ProvisioningAccessPointName { get; set; }
        // Password for the temporary setup access point and setup form.
        public string ProvisioningPassword { get; set; }
        // Local web address used by the setup portal.
        public string ProvisioningAddress { get; set; }
        // Legacy friendly setup host name kept for saved configuration compatibility.
        public string ProvisioningHostName { get; set; }
        // Setup window length after boot.
        public int ProvisioningWindowMilliseconds { get; set; }
        // True when the next boot should enter setup mode before meteo mode.
        public bool ProvisioningRequestedOnNextBoot { get; set; }
    }

    /// <summary>
    /// Describes an immutable station identity sent to the server.
    /// </summary>
    public sealed class StationIdentity
    {
        // User-facing station identifier.
        public string StationId { get; set; }
        // Permanent station GUID.
        public string StationGuid { get; set; }
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
        private const string DefaultProvisioningPassword = "12345678";
        private const string DefaultProvisioningAddress = "192.168.4.1";

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

                if (configuration.ProvisioningAccessPointName == null || configuration.ProvisioningAccessPointName.Length == 0)
                {
                    configuration.ProvisioningAccessPointName = BoardProfile.ProvisioningAccessPointName;
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Setup SSID doplneno do konfigurace.");
                }

                if (configuration.ProvisioningPassword == null || configuration.ProvisioningPassword.Length < 8)
                {
                    configuration.ProvisioningPassword = DefaultProvisioningPassword;
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Setup heslo opraveno na vychozi hodnotu.");
                }

                if (configuration.ProvisioningAddress == null || configuration.ProvisioningAddress.Length == 0)
                {
                    configuration.ProvisioningAddress = DefaultProvisioningAddress;
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Setup IP adresa doplnena do konfigurace.");
                }

                if (IsUnknownHardwareId(configuration.DeviceHardwareId))
                {
                    configuration.DeviceHardwareId = CreateHardwareId();
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Device hardware ID vytvoren a ulozen: " + configuration.DeviceHardwareId);
                }

                if (configuration.StationGuid == null || configuration.StationGuid.Length == 0)
                {
                    configuration.StationGuid = CreateStationGuid();
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Station GUID vytvoren a ulozen: " + configuration.StationGuid);
                }

                bool upgraded = EnsureRuntimeDefaults(configuration);
                if (upgraded)
                {
                    _configurationStore.Save(configuration);
                    Debug.WriteLine("Runtime nastaveni doplneno do konfigurace.");
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
            configuration.StationGuid = CreateStationGuid();
            configuration.DeviceHardwareId = CreateHardwareId();
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
            configuration.ApiKey = string.Empty;
            configuration.MeasurementIntervalMinutes = 10;
            configuration.SamplePeriodSeconds = 1;
            configuration.MeasurementUploadsPerCycle = 2;
            configuration.EventUploadsPerCycle = 4;
            configuration.LocationText = string.Empty;
            configuration.ProvisioningAccessPointName = BoardProfile.ProvisioningAccessPointName;
            configuration.ProvisioningPassword = DefaultProvisioningPassword;
            configuration.ProvisioningAddress = DefaultProvisioningAddress;
            configuration.ProvisioningHostName = "meteo";
            configuration.ProvisioningWindowMilliseconds = DefaultProvisioningWindowMilliseconds;
            configuration.ProvisioningRequestedOnNextBoot = false;
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
                && configuration.ProvisioningWindowMilliseconds > 0;
        }

        public static string NormalizeApiBaseUrl(string apiBaseUrl)
        {
            if (apiBaseUrl == null)
            {
                return string.Empty;
            }

            string normalized = apiBaseUrl.Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            if (normalized.IndexOf("://") < 0)
            {
                normalized = "http://" + normalized;
            }

            int hostStart = normalized.IndexOf("://") + 3;
            int pathStart = normalized.IndexOf("/", hostStart);
            int hostEnd = pathStart < 0 ? normalized.Length : pathStart;
            string host = normalized.Substring(hostStart, hostEnd - hostStart);
            if (host.IndexOf(":") < 0)
            {
                normalized = normalized.Substring(0, hostEnd) + ":5181" + normalized.Substring(hostEnd);
            }

            pathStart = normalized.IndexOf("/", hostStart);
            if (pathStart < 0)
            {
                normalized += "/api";
            }
            else if (pathStart == normalized.Length - 1)
            {
                normalized = normalized.Substring(0, normalized.Length - 1) + "/api";
            }

            while (normalized.Length > 0 && normalized[normalized.Length - 1] == '/')
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        private static bool EnsureRuntimeDefaults(StationConfiguration configuration)
        {
            bool changed = false;

            if (configuration.MeasurementIntervalMinutes < 1)
            {
                configuration.MeasurementIntervalMinutes = 10;
                changed = true;
            }

            if (configuration.SamplePeriodSeconds < 1)
            {
                configuration.SamplePeriodSeconds = 1;
                changed = true;
            }

            if (configuration.MeasurementUploadsPerCycle < 1)
            {
                configuration.MeasurementUploadsPerCycle = 2;
                changed = true;
            }

            if (configuration.EventUploadsPerCycle < 1)
            {
                configuration.EventUploadsPerCycle = 4;
                changed = true;
            }

            if (configuration.LocationText == null)
            {
                configuration.LocationText = string.Empty;
                changed = true;
            }

            if (configuration.ApiKey == null)
            {
                configuration.ApiKey = string.Empty;
                changed = true;
            }

            string normalizedApiBaseUrl = NormalizeApiBaseUrl(configuration.ApiBaseUrl);
            if (configuration.ApiBaseUrl != normalizedApiBaseUrl)
            {
                configuration.ApiBaseUrl = normalizedApiBaseUrl;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Creates a station GUID suitable for permanent flash storage.
        /// </summary>
        private static string CreateStationGuid()
        {
            return Guid.NewGuid().ToString();
        }

        private static string CreateHardwareId()
        {
            return BoardProfile.HardwareIdPrefix + "-" + Guid.NewGuid().ToString();
        }

        private static bool IsUnknownHardwareId(string hardwareId)
        {
            return hardwareId == null
                || hardwareId.Length == 0
                || hardwareId == BoardProfile.HardwareIdPrefix + "-UNKNOWN";
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
            identity.StationGuid = configuration.StationGuid;
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
            Debug.WriteLine("Mereni interval: " + configuration.MeasurementIntervalMinutes + " min.");
            Debug.WriteLine("Vzorkovani: " + configuration.SamplePeriodSeconds + " s.");
            Debug.WriteLine("Lokace: " + (configuration.LocationText == null || configuration.LocationText.Length == 0 ? "<nenastaveno>" : configuration.LocationText));
        }
    }
}
