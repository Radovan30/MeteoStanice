using System.Diagnostics;
using System.IO;

namespace MeteoStanice
{
    /// <summary>
    /// Provides durable storage for station configuration.
    /// </summary>
    public interface IStationConfigurationStore
    {
        /// <summary>
        /// Attempts to load the last saved station configuration.
        /// </summary>
        bool TryLoad(out StationConfiguration configuration);

        /// <summary>
        /// Saves station configuration after it has been validated.
        /// </summary>
        bool Save(StationConfiguration configuration);
    }

    /// <summary>
    /// Keeps configuration in RAM for desktop and firmware smoke tests.
    /// </summary>
    public sealed class InMemoryStationConfigurationStore : IStationConfigurationStore
    {
        // Last configuration saved in this process.
        private StationConfiguration _configuration;
        // Distinguishes "not saved" from a null reference.
        private bool _hasConfiguration;

        /// <summary>
        /// Attempts to read configuration saved during this process lifetime.
        /// </summary>
        public bool TryLoad(out StationConfiguration configuration)
        {
            configuration = _configuration;
            return _hasConfiguration;
        }

        /// <summary>
        /// Stores configuration in RAM.
        /// </summary>
        public bool Save(StationConfiguration configuration)
        {
            _configuration = configuration;
            _hasConfiguration = configuration != null;
            return _hasConfiguration;
        }
    }

    /// <summary>
    /// Saves station configuration to ESP32 internal flash storage.
    /// </summary>
    public sealed class FlashStationConfigurationStore : IStationConfigurationStore
    {
        // Internal ESP32 storage path handled by nanoFramework littlefs.
        private const string ConfigurationPath = @"I:\meteo.cfg";

        /// <summary>
        /// Attempts to load configuration from internal flash storage.
        /// </summary>
        public bool TryLoad(out StationConfiguration configuration)
        {
            configuration = null;

            try
            {
                if (!File.Exists(ConfigurationPath))
                {
                    Debug.WriteLine("Konfigurace ve flash zatim neexistuje.");
                    return false;
                }

                string content = File.ReadAllText(ConfigurationPath);
                configuration = Deserialize(content);
                Debug.WriteLine("Konfigurace nactena z flash.");
                return configuration != null;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("Konfiguraci z flash nelze nacist: " + exception.Message);
                configuration = null;
                return false;
            }
        }

        /// <summary>
        /// Saves configuration to internal flash storage.
        /// </summary>
        public bool Save(StationConfiguration configuration)
        {
            if (configuration == null)
            {
                return false;
            }

            try
            {
                File.WriteAllText(ConfigurationPath, Serialize(configuration));
                Debug.WriteLine("Konfigurace ulozena do flash: " + ConfigurationPath);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("Konfiguraci do flash nelze ulozit: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Converts configuration into a small key-value text file.
        /// </summary>
        private static string Serialize(StationConfiguration configuration)
        {
            return "StationId=" + Escape(configuration.StationId) + "\n"
                + "StationGuid=" + Escape(configuration.StationGuid) + "\n"
                + "DeviceHardwareId=" + Escape(configuration.DeviceHardwareId) + "\n"
                + "StationName=" + Escape(configuration.StationName) + "\n"
                + "FirmwareVersion=" + Escape(configuration.FirmwareVersion) + "\n"
                + "WifiSsid=" + Escape(configuration.WifiSsid) + "\n"
                + "WifiPassword=" + Escape(configuration.WifiPassword) + "\n"
                + "UseDhcp=" + (configuration.UseDhcp ? "1" : "0") + "\n"
                + "StaticIpAddress=" + Escape(configuration.StaticIpAddress) + "\n"
                + "GatewayAddress=" + Escape(configuration.GatewayAddress) + "\n"
                + "SubnetMask=" + Escape(configuration.SubnetMask) + "\n"
                + "DnsAddress=" + Escape(configuration.DnsAddress) + "\n"
                + "ApiBaseUrl=" + Escape(configuration.ApiBaseUrl) + "\n"
                + "ApiKey=" + Escape(configuration.ApiKey) + "\n"
                + "MeasurementIntervalMinutes=" + configuration.MeasurementIntervalMinutes + "\n"
                + "SamplePeriodSeconds=" + configuration.SamplePeriodSeconds + "\n"
                + "MeasurementUploadsPerCycle=" + configuration.MeasurementUploadsPerCycle + "\n"
                + "EventUploadsPerCycle=" + configuration.EventUploadsPerCycle + "\n"
                + "LocationText=" + Escape(configuration.LocationText) + "\n"
                + "ProvisioningAccessPointName=" + Escape(configuration.ProvisioningAccessPointName) + "\n"
                + "ProvisioningPassword=" + Escape(configuration.ProvisioningPassword) + "\n"
                + "ProvisioningAddress=" + Escape(configuration.ProvisioningAddress) + "\n"
                + "ProvisioningHostName=" + Escape(configuration.ProvisioningHostName) + "\n"
                + "ProvisioningWindowMilliseconds=" + configuration.ProvisioningWindowMilliseconds + "\n"
                + "ProvisioningRequestedOnNextBoot=" + (configuration.ProvisioningRequestedOnNextBoot ? "1" : "0") + "\n";
        }

        /// <summary>
        /// Creates configuration from a key-value text file.
        /// </summary>
        private static StationConfiguration Deserialize(string content)
        {
            if (content == null || content.Length == 0)
            {
                return null;
            }

            StationConfiguration configuration = new StationConfiguration();
            configuration.StationId = GetValue(content, "StationId");
            configuration.StationGuid = GetValue(content, "StationGuid");
            configuration.DeviceHardwareId = GetValue(content, "DeviceHardwareId");
            configuration.StationName = GetValue(content, "StationName");
            configuration.FirmwareVersion = GetValue(content, "FirmwareVersion");
            configuration.WifiSsid = GetValue(content, "WifiSsid");
            configuration.WifiPassword = GetValue(content, "WifiPassword");
            configuration.UseDhcp = GetValue(content, "UseDhcp") == "1";
            configuration.StaticIpAddress = GetValue(content, "StaticIpAddress");
            configuration.GatewayAddress = GetValue(content, "GatewayAddress");
            configuration.SubnetMask = GetValue(content, "SubnetMask");
            configuration.DnsAddress = GetValue(content, "DnsAddress");
            configuration.ApiBaseUrl = StationConfigurationService.NormalizeApiBaseUrl(GetValue(content, "ApiBaseUrl"));
            configuration.ApiKey = GetValue(content, "ApiKey");
            configuration.MeasurementIntervalMinutes = ParseInt(GetValue(content, "MeasurementIntervalMinutes"), 10);
            configuration.SamplePeriodSeconds = ParseInt(GetValue(content, "SamplePeriodSeconds"), 1);
            configuration.MeasurementUploadsPerCycle = ParseInt(GetValue(content, "MeasurementUploadsPerCycle"), 2);
            configuration.EventUploadsPerCycle = ParseInt(GetValue(content, "EventUploadsPerCycle"), 4);
            configuration.LocationText = GetValue(content, "LocationText");
            configuration.ProvisioningAccessPointName = GetValue(content, "ProvisioningAccessPointName");
            configuration.ProvisioningPassword = GetValue(content, "ProvisioningPassword");
            configuration.ProvisioningAddress = GetValue(content, "ProvisioningAddress");
            configuration.ProvisioningHostName = GetValue(content, "ProvisioningHostName");
            configuration.ProvisioningWindowMilliseconds = ParseInt(GetValue(content, "ProvisioningWindowMilliseconds"), 30000);
            configuration.ProvisioningRequestedOnNextBoot = GetValue(content, "ProvisioningRequestedOnNextBoot") == "1";
            return configuration;
        }

        /// <summary>
        /// Finds and unescapes one value from the configuration text.
        /// </summary>
        private static string GetValue(string content, string key)
        {
            string marker = key + "=";
            int start = content.IndexOf(marker);
            if (start < 0)
            {
                return string.Empty;
            }

            start += marker.Length;
            int end = content.IndexOf("\n", start);
            if (end < 0)
            {
                end = content.Length;
            }

            string value = content.Substring(start, end - start);
            if (value.EndsWith("\r"))
            {
                value = value.Substring(0, value.Length - 1);
            }

            return Unescape(value);
        }

        /// <summary>
        /// Parses integer values without throwing from invalid stored data.
        /// </summary>
        private static int ParseInt(string value, int defaultValue)
        {
            if (value == null || value.Length == 0)
            {
                return defaultValue;
            }

            int parsed = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character < '0' || character > '9')
                {
                    return defaultValue;
                }

                parsed = (parsed * 10) + (character - '0');
            }

            return parsed;
        }

        /// <summary>
        /// Escapes separators used by the key-value file.
        /// </summary>
        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string escaped = string.Empty;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '\\')
                {
                    escaped += "\\\\";
                }
                else if (character == '\r')
                {
                    escaped += "\\r";
                }
                else if (character == '\n')
                {
                    escaped += "\\n";
                }
                else
                {
                    escaped += character;
                }
            }

            return escaped;
        }

        /// <summary>
        /// Restores escaped values from the key-value file.
        /// </summary>
        private static string Unescape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string unescaped = string.Empty;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '\\' && index + 1 < value.Length)
                {
                    char next = value[index + 1];
                    if (next == 'n')
                    {
                        unescaped += '\n';
                        index++;
                    }
                    else if (next == 'r')
                    {
                        unescaped += '\r';
                        index++;
                    }
                    else if (next == '\\')
                    {
                        unescaped += '\\';
                        index++;
                    }
                    else
                    {
                        unescaped += character;
                    }
                }
                else
                {
                    unescaped += character;
                }
            }

            return unescaped;
        }
    }
}
