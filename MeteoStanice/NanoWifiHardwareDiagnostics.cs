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
                Debug.WriteLine("Real Wi-Fi connect target SSID='" + configuration.WifiSsid + "', password length=" + (configuration.WifiPassword == null ? 0 : configuration.WifiPassword.Length));
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                if (adapters == null || adapters.Length == 0)
                {
                    Debug.WriteLine("Real Wi-Fi connect failed: no adapter.");
                    return false;
                }

                WifiAdapter adapter = adapters[0];
                adapter.SetDeviceName(configuration.StationName);
                ApplyMeteoIpConfiguration(configuration);

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    WifiConnectionResult result = adapter.Connect(configuration.WifiSsid, WifiReconnectionKind.Automatic, configuration.WifiPassword);
                    Debug.WriteLine("Real Wi-Fi connect attempt " + attempt + " status: " + result.ConnectionStatus.ToString());
                    if (result.ConnectionStatus == WifiConnectionStatus.Success && WaitForValidWirelessIp(12000))
                    {
                        return true;
                    }

                    Thread.Sleep(1000);
                }

                return false;
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

        /// <summary>
        /// Returns the current IPv4 address from the wireless client interface.
        /// </summary>
        public string GetWirelessIpAddress()
        {
            try
            {
                NetworkInterface wirelessInterface = FindWirelessClientInterface();
                if (wirelessInterface != null && IsValidIp(wirelessInterface.IPv4Address))
                {
                    return wirelessInterface.IPv4Address;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Wireless IP read failed: " + exception.Message);
            }

            return "0.0.0.0";
        }

        /// <summary>
        /// Applies DHCP or static IPv4 settings before connecting in meteo mode.
        /// </summary>
        private void ApplyMeteoIpConfiguration(StationConfiguration configuration)
        {
            NetworkInterface wirelessInterface = FindWirelessClientInterface();
            if (wirelessInterface == null)
            {
                Debug.WriteLine("Meteo IP config skipped: Wireless80211 interface nenalezeno.");
                return;
            }

            try
            {
                if (configuration.UseDhcp)
                {
                    wirelessInterface.EnableDhcp();
                    Debug.WriteLine("Meteo IP config: DHCP.");
                    return;
                }

                if (!IsValidIp(configuration.StaticIpAddress)
                    || !IsValidIp(configuration.SubnetMask)
                    || !IsValidIp(configuration.GatewayAddress))
                {
                    Debug.WriteLine("Meteo IP config skipped: staticka IPv4 konfigurace neni kompletni.");
                    return;
                }

                wirelessInterface.EnableStaticIPv4(configuration.StaticIpAddress, configuration.SubnetMask, configuration.GatewayAddress);
                if (IsValidIp(configuration.DnsAddress))
                {
                    string[] dnsAddresses = new string[1];
                    dnsAddresses[0] = configuration.DnsAddress;
                    wirelessInterface.EnableStaticIPv4Dns(dnsAddresses);
                }

                Debug.WriteLine("Meteo IP config: static IP=" + configuration.StaticIpAddress
                    + ", gateway=" + configuration.GatewayAddress
                    + ", DNS=" + configuration.DnsAddress);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Meteo IP config failed: " + exception.Message);
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
