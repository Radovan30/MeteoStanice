using System;
using System.Device.Wifi;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Runs minimal diagnostics against the real nanoFramework Wi-Fi adapter API.
    /// </summary>
    public sealed class NanoWifiHardwareDiagnostics
    {
        private bool _scanCompleted;

        /// <summary>
        /// Prints the number of Wi-Fi adapters visible to the firmware.
        /// </summary>
        public bool PrintAdapterCount()
        {
            try
            {
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                int count = adapters == null ? 0 : adapters.Length;
                Debug.WriteLine("Real Wi-Fi adapters: " + count);
                return count > 0;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Real Wi-Fi diagnostics failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Runs a short Wi-Fi scan and prints discovered network names.
        /// </summary>
        public bool ScanAndPrintNetworks()
        {
            try
            {
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                if (adapters == null || adapters.Length == 0)
                {
                    Debug.WriteLine("Real Wi-Fi scan skipped: no adapter.");
                    return false;
                }

                WifiAdapter adapter = adapters[0];
                _scanCompleted = false;
                adapter.AvailableNetworksChanged += OnAvailableNetworksChanged;
                adapter.ScanAsync();

                int waitCount = 0;
                while (!_scanCompleted && waitCount < 20)
                {
                    Thread.Sleep(250);
                    waitCount++;
                }

                adapter.AvailableNetworksChanged -= OnAvailableNetworksChanged;
                PrintNetworkReport(adapter);
                return _scanCompleted;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Real Wi-Fi scan failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Attempts to connect to configured Wi-Fi when SSID is available.
        /// </summary>
        public bool TryConnect(StationConfiguration configuration)
        {
            if (configuration == null || configuration.WifiSsid == null || configuration.WifiSsid.Length == 0)
            {
                Debug.WriteLine("Real Wi-Fi connect skipped: SSID empty.");
                return false;
            }

            try
            {
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                if (adapters == null || adapters.Length == 0)
                {
                    Debug.WriteLine("Real Wi-Fi connect failed: no adapter.");
                    return false;
                }

                WifiAdapter adapter = adapters[0];
                adapter.SetDeviceName(configuration.StationName);
                WifiConnectionResult result = adapter.Connect(configuration.WifiSsid, WifiReconnectionKind.Automatic, configuration.WifiPassword);
                Debug.WriteLine("Real Wi-Fi connect status: " + result.ConnectionStatus.ToString());
                return result.ConnectionStatus == WifiConnectionStatus.Success;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Real Wi-Fi connect failed: " + exception.Message);
                return false;
            }
        }

        private void OnAvailableNetworksChanged(WifiAdapter sender, object e)
        {
            _scanCompleted = true;
        }

        private void PrintNetworkReport(WifiAdapter adapter)
        {
            WifiNetworkReport report = adapter.NetworkReport;
            if (report == null || report.AvailableNetworks == null)
            {
                Debug.WriteLine("Real Wi-Fi networks: report empty.");
                return;
            }

            int count = report.AvailableNetworks.Length;
            Debug.WriteLine("Real Wi-Fi networks: " + count);

            int max = count > 3 ? 3 : count;
            for (int index = 0; index < max; index++)
            {
                WifiAvailableNetwork network = report.AvailableNetworks[index];
                Debug.WriteLine("Wi-Fi network " + index + ": " + network.Ssid + " RSSI=" + network.NetworkRssiInDecibelMilliwatts);
            }
        }
    }
}
