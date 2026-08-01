using System;
using System.Device.Wifi;
using System.Diagnostics;
using System.Net.NetworkInformation;
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
                if (result.ConnectionStatus != WifiConnectionStatus.Success)
                {
                    return false;
                }

                return WaitForValidWirelessIp(10000);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Real Wi-Fi connect failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Prints all network interfaces and their IPv4 configuration.
        /// </summary>
        public void PrintAllNetworkInterfaces()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                if (interfaces == null || interfaces.Length == 0)
                {
                    Debug.WriteLine("Network interfaces: none.");
                    return;
                }

                Debug.WriteLine("Network interfaces: " + interfaces.Length);
                for (int index = 0; index < interfaces.Length; index++)
                {
                    PrintNetworkInterface(interfaces[index]);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Network interface diagnostics failed: " + exception.Message);
            }
        }

        private void OnAvailableNetworksChanged(WifiAdapter sender, object e)
        {
            _scanCompleted = true;
        }

        /// <summary>
        /// Waits until the wireless client interface has a non-empty IPv4 address.
        /// </summary>
        private bool WaitForValidWirelessIp(int timeoutMilliseconds)
        {
            int elapsedMilliseconds = 0;
            while (elapsedMilliseconds < timeoutMilliseconds)
            {
                NetworkInterface wirelessInterface = FindWirelessClientInterface();
                if (wirelessInterface != null && IsValidIp(wirelessInterface.IPv4Address))
                {
                    Debug.WriteLine("Real Wi-Fi IP ready: " + wirelessInterface.IPv4Address);
                    return true;
                }

                Thread.Sleep(500);
                elapsedMilliseconds += 500;
            }

            Debug.WriteLine("Real Wi-Fi IP timeout.");
            return false;
        }

        /// <summary>
        /// Finds the Wi-Fi client network interface.
        /// </summary>
        private NetworkInterface FindWirelessClientInterface()
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (interfaces == null)
            {
                return null;
            }

            for (int index = 0; index < interfaces.Length; index++)
            {
                if (interfaces[index].NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    return interfaces[index];
                }
            }

            return null;
        }

        /// <summary>
        /// Checks whether the IP address looks usable for meteo mode.
        /// </summary>
        private bool IsValidIp(string address)
        {
            return address != null
                && address.Length > 0
                && address != "0.0.0.0";
        }

        /// <summary>
        /// Prints one network interface IPv4 state.
        /// </summary>
        private void PrintNetworkInterface(NetworkInterface networkInterface)
        {
            if (networkInterface == null)
            {
                Debug.WriteLine("Network interface: null.");
                return;
            }

            Debug.WriteLine("Network interface type=" + networkInterface.NetworkInterfaceType.ToString()
                + ", DHCP=" + networkInterface.IsDhcpEnabled
                + ", IP=" + networkInterface.IPv4Address
                + ", mask=" + networkInterface.IPv4SubnetMask
                + ", gateway=" + networkInterface.IPv4GatewayAddress);

            string[] dnsAddresses = networkInterface.IPv4DnsAddresses;
            if (dnsAddresses == null || dnsAddresses.Length == 0)
            {
                Debug.WriteLine("Network interface DNS=<nenastaveno>");
                return;
            }

            for (int index = 0; index < dnsAddresses.Length; index++)
            {
                Debug.WriteLine("Network interface DNS " + index + "=" + dnsAddresses[index]);
            }
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
