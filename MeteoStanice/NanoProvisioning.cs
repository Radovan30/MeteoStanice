using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Describes simulated setup-window outcome.
    /// </summary>
    public sealed class NanoProvisioningResult
    {
        // True when a client connected during the setup window.
        public bool ClientConnected { get; set; }
        // True when new configuration was saved.
        public bool ConfigurationSaved { get; set; }
        // True when setup window elapsed without saving.
        public bool TimedOut { get; set; }
        // Configuration produced by the setup window.
        public StationConfiguration Configuration { get; set; }
        // Short diagnostic message.
        public string Message { get; set; }
        // True when SoftAP configuration was saved and another reboot is required.
        public bool RebootRequired { get; set; }
    }

    /// <summary>
    /// Simulates the temporary setup portal without Wi-Fi or web libraries.
    /// </summary>
    public sealed class NanoProvisioningService
    {
        private readonly IStationConfigurationStore _configurationStore;
        private readonly StationConfigurationService _configurationService;
        private readonly IProvisioningPortal _provisioningPortal;

        /// <summary>
        /// Creates provisioning service.
        /// </summary>
        public NanoProvisioningService(IStationConfigurationStore configurationStore, StationConfigurationService configurationService)
        {
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }

        /// <summary>
        /// Creates provisioning service with a real setup portal.
        /// </summary>
        public NanoProvisioningService(IStationConfigurationStore configurationStore, StationConfigurationService configurationService, IProvisioningPortal provisioningPortal)
            : this(configurationStore, configurationService)
        {
            _provisioningPortal = provisioningPortal;
        }

        /// <summary>
        /// Runs simulated 30 second setup window and returns active configuration.
        /// </summary>
        public StationConfiguration RunStartupWindow(StationConfiguration currentConfiguration)
        {
            NanoProvisioningResult result = RunStartupWindowWithResult(currentConfiguration);
            return result.Configuration;
        }

        /// <summary>
        /// Runs setup window and returns the complete result for boot orchestration.
        /// </summary>
        public NanoProvisioningResult RunStartupWindowWithResult(StationConfiguration currentConfiguration)
        {
            NanoProvisioningResult result = WaitForResult(currentConfiguration);

            if (result.ConfigurationSaved && _configurationService.Validate(result.Configuration))
            {
                _configurationStore.Save(result.Configuration);
                Debug.WriteLine("Provisioning ulozil novou konfiguraci.");
                Debug.WriteLine("Provisioning meteo Wi-Fi: " + (result.Configuration.WifiSsid == string.Empty ? "<nenastaveno>" : result.Configuration.WifiSsid));
                Debug.WriteLine("Provisioning meteo IP: " + (result.Configuration.UseDhcp ? "DHCP" : result.Configuration.StaticIpAddress));
                StationConfiguration verifyConfiguration;
                if (_configurationStore.TryLoad(out verifyConfiguration))
                {
                    Debug.WriteLine("Provisioning kontrola flash SSID: " + (verifyConfiguration.WifiSsid == string.Empty ? "<nenastaveno>" : verifyConfiguration.WifiSsid));
                    Debug.WriteLine("Provisioning kontrola flash heslo delka: " + (verifyConfiguration.WifiPassword == null ? 0 : verifyConfiguration.WifiPassword.Length));
                }

                return result;
            }

            if (result.TimedOut)
            {
                Debug.WriteLine("Provisioning timeout, stanice pokracuje v meteo rezimu.");
            }

            result.Configuration = currentConfiguration;
            return result;
        }

        /// <summary>
        /// Waits for the configured setup window length and then returns to meteo mode.
        /// </summary>
        private NanoProvisioningResult WaitForResult(StationConfiguration currentConfiguration)
        {
            if (_provisioningPortal != null)
            {
                return WaitForPortalResult(currentConfiguration);
            }

            int timeoutMilliseconds = currentConfiguration.ProvisioningWindowMilliseconds;
            int elapsedMilliseconds = 0;
            int nextLogMilliseconds = 0;

            Debug.WriteLine("Provisioning okno otevreno.");
            while (elapsedMilliseconds < timeoutMilliseconds)
            {
                if (elapsedMilliseconds >= nextLogMilliseconds)
                {
                    Debug.WriteLine("Provisioning ceka, zbyva ms: " + (timeoutMilliseconds - elapsedMilliseconds));
                    nextLogMilliseconds += 5000;
                }

                Thread.Sleep(250);
                elapsedMilliseconds += 250;
            }

            NanoProvisioningResult result = new NanoProvisioningResult();
            result.ClientConnected = false;
            result.ConfigurationSaved = false;
            result.TimedOut = true;
            result.Configuration = currentConfiguration;
            result.Message = "Setup window elapsed without saved changes.";
            return result;
        }

        /// <summary>
        /// Starts the real provisioning portal and maps its result to the boot-safe result.
        /// </summary>
        private NanoProvisioningResult WaitForPortalResult(StationConfiguration currentConfiguration)
        {
            NanoProvisioningResult result = new NanoProvisioningResult();
            result.Configuration = currentConfiguration;
            bool portalStarted = false;

            try
            {
                Debug.WriteLine("Provisioning portal startuje.");
                if (!_provisioningPortal.Start(currentConfiguration))
                {
                    Debug.WriteLine("Provisioning portal nelze spustit, stanice pokracuje v meteo rezimu.");
                    result.TimedOut = true;
                    result.RebootRequired = true;
                    result.Message = "Setup portal did not start.";
                    return result;
                }

                portalStarted = true;
                ProvisioningWindowResult portalResult = _provisioningPortal.WaitForResult(currentConfiguration.ProvisioningWindowMilliseconds);
                result.ClientConnected = portalResult.ClientConnected;
                result.ConfigurationSaved = portalResult.ConfigurationSaved;
                result.TimedOut = portalResult.TimedOut;
                result.Configuration = portalResult.Configuration;
                result.Message = portalResult.Message;
                return result;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("Provisioning portal selhal: " + exception.Message);
                result.TimedOut = true;
                result.ConfigurationSaved = false;
                result.Message = "Setup portal failed.";
                return result;
            }
            finally
            {
                if (portalStarted)
                {
                    _provisioningPortal.Stop();
                }
            }
        }
    }
}
