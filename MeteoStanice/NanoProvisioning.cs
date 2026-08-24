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
        // True when setup mode was closed by pressing the setup button again.
        public bool CancelledByButton { get; set; }
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
            return ApplyResult(currentConfiguration, result);
        }

        /// <summary>
        /// Runs setup mode until the user saves settings or closes setup by button.
        /// </summary>
        public NanoProvisioningResult RunUntilSavedOrCancelled(StationConfiguration currentConfiguration, NanoTtgoButtons buttons)
        {
            NanoProvisioningResult result = WaitForPortalResultUntilCancelled(currentConfiguration, buttons);
            return ApplyResult(currentConfiguration, result);
        }

        private NanoProvisioningResult ApplyResult(StationConfiguration currentConfiguration, NanoProvisioningResult result)
        {
            if (result.ConfigurationSaved && _configurationService.Validate(result.Configuration))
            {
                if (!SaveAndVerify(result.Configuration))
                {
                    Debug.WriteLine("Provisioning konfiguraci neulozil, stanice ponecha puvodni konfiguraci.");
                    result.ConfigurationSaved = false;
                    result.TimedOut = false;
                    result.Configuration = currentConfiguration;
                    result.Message = "Setup konfiguraci se nepodarilo ulozit do flash.";
                    return result;
                }

                Debug.WriteLine("Provisioning ulozil novou konfiguraci.");
                Debug.WriteLine("Provisioning meteo Wi-Fi: " + (result.Configuration.WifiSsid == string.Empty ? "<nenastaveno>" : result.Configuration.WifiSsid));
                Debug.WriteLine("Provisioning meteo IP: " + (result.Configuration.UseDhcp ? "DHCP" : result.Configuration.StaticIpAddress));
                return result;
            }

            if (result.CancelledByButton)
            {
                Debug.WriteLine("Provisioning ukoncen tlacitkem, stanice pokracuje v meteo rezimu.");
            }

            if (result.TimedOut)
            {
                Debug.WriteLine("Provisioning timeout, stanice pokracuje v meteo rezimu.");
            }

            result.Configuration = currentConfiguration;
            return result;
        }

        private bool SaveAndVerify(StationConfiguration configuration)
        {
            if (!_configurationStore.Save(configuration))
            {
                Debug.WriteLine("Provisioning save failed: Save vratil false.");
                return false;
            }

            StationConfiguration verifyConfiguration;
            if (!_configurationStore.TryLoad(out verifyConfiguration))
            {
                Debug.WriteLine("Provisioning save failed: konfiguraci nelze po ulozeni nacist.");
                return false;
            }

            Debug.WriteLine("Provisioning kontrola flash SSID: " + (verifyConfiguration.WifiSsid == string.Empty ? "<nenastaveno>" : verifyConfiguration.WifiSsid));
            Debug.WriteLine("Provisioning kontrola flash heslo delka: " + (verifyConfiguration.WifiPassword == null ? 0 : verifyConfiguration.WifiPassword.Length));

            return SameValue(configuration.WifiSsid, verifyConfiguration.WifiSsid)
                && SameValue(configuration.WifiPassword, verifyConfiguration.WifiPassword)
                && SameValue(configuration.ApiBaseUrl, verifyConfiguration.ApiBaseUrl)
                && SameValue(configuration.ApiKey, verifyConfiguration.ApiKey);
        }

        private static bool SameValue(string left, string right)
        {
            if (left == null)
            {
                left = string.Empty;
            }

            if (right == null)
            {
                right = string.Empty;
            }

            return left == right;
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
                result.CancelledByButton = portalResult.CancelledByButton;
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

        private NanoProvisioningResult WaitForPortalResultUntilCancelled(StationConfiguration currentConfiguration, NanoTtgoButtons buttons)
        {
            NanoProvisioningResult result = new NanoProvisioningResult();
            result.Configuration = currentConfiguration;
            bool portalStarted = false;

            try
            {
                Debug.WriteLine("Provisioning portal startuje v rucnim setup rezimu.");
                if (_provisioningPortal == null || !_provisioningPortal.Start(currentConfiguration))
                {
                    Debug.WriteLine("Provisioning portal nelze spustit v rucnim setup rezimu.");
                    result.TimedOut = false;
                    result.RebootRequired = true;
                    result.Message = "Setup portal did not start.";
                    return result;
                }

                portalStarted = true;
                ProvisioningWindowResult portalResult = _provisioningPortal.WaitForResultUntilCancelled(buttons);
                result.ClientConnected = portalResult.ClientConnected;
                result.ConfigurationSaved = portalResult.ConfigurationSaved;
                result.TimedOut = portalResult.TimedOut;
                result.CancelledByButton = portalResult.CancelledByButton;
                result.Configuration = portalResult.Configuration;
                result.Message = portalResult.Message;
                return result;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("Provisioning portal selhal v rucnim setup rezimu: " + exception.Message);
                result.TimedOut = false;
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
