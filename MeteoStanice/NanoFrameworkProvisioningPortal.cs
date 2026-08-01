using System;
using System.Collections;
using System.Device.Wifi;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Iot.Device.DhcpServer;

namespace MeteoStanice
{
    /// <summary>
    /// Runs a nanoFramework SoftAP and HTTP setup form for local station configuration.
    /// </summary>
    public sealed class NanoFrameworkProvisioningPortal : IProvisioningPortal
    {
        private const string SubnetMask = "255.255.255.0";

        private HttpListener _listener;
        private Thread _serverThread;
        private StationConfiguration _baseConfiguration;
        private ProvisioningWindowResult _result;
        private bool _running;
        private bool _scanCompleted;

        /// <summary>
        /// Starts the SoftAP, DHCP server and web server.
        /// </summary>
        public bool Start(StationConfiguration configuration)
        {
            _baseConfiguration = CloneConfiguration(configuration);
            _result = new ProvisioningWindowResult();
            _running = true;

            if (!ConfigureAccessPoint(configuration))
            {
                Debug.WriteLine("SoftAP konfigurace byla ulozena, pro aktivaci je nutny restart.");
                return false;
            }

            DhcpServer dhcpServer = new DhcpServer();
            dhcpServer.CaptivePortalUrl = "http://" + configuration.ProvisioningAddress + "/";
            if (!dhcpServer.Start(IPAddress.Parse(configuration.ProvisioningAddress), IPAddress.Parse(SubnetMask)))
            {
                Debug.WriteLine("DHCP server pro provisioning se nepodarilo spustit.");
                return false;
            }

            _listener = new HttpListener("http");
            _serverThread = new Thread(RunServer);
            _serverThread.Start();

            Debug.WriteLine("Provisioning portal bezi na http://" + configuration.ProvisioningAddress + "/");
            return true;
        }

        /// <summary>
        /// Waits until settings are saved or the setup window expires.
        /// </summary>
        public ProvisioningWindowResult WaitForResult(int timeoutMilliseconds)
        {
            int elapsed = 0;
            while (_running && elapsed < timeoutMilliseconds)
            {
                if (_result != null && _result.ConfigurationSaved)
                {
                    return _result;
                }

                Thread.Sleep(250);
                elapsed += 250;
            }

            if (_result == null)
            {
                _result = new ProvisioningWindowResult();
            }

            _result.TimedOut = true;
            _result.ConfigurationSaved = false;
            _result.Configuration = _baseConfiguration;
            _result.Message = "Setup okno vyprselo bez ulozeni.";
            return _result;
        }

        /// <summary>
        /// Stops the web listener and disables SoftAP mode.
        /// </summary>
        public void Stop()
        {
            _running = false;

            if (_listener != null)
            {
                _listener.Stop();
                _listener.Close();
                _listener = null;
            }

            DisableAccessPoint();
        }

        private bool ConfigureAccessPoint(StationConfiguration configuration)
        {
            NetworkInterface networkInterface = GetAccessPointInterface();
            if (networkInterface == null)
            {
                Debug.WriteLine("WirelessAP interface nebyl nalezen.");
                return false;
            }

            WirelessAPConfiguration accessPointConfiguration = GetAccessPointConfiguration(networkInterface);
            if (accessPointConfiguration == null)
            {
                Debug.WriteLine("WirelessAP konfigurace nebyla nalezena.");
                return false;
            }

            if (accessPointConfiguration.Options == (WirelessAPConfiguration.ConfigurationOptions.Enable | WirelessAPConfiguration.ConfigurationOptions.AutoStart)
                && networkInterface.IPv4Address == configuration.ProvisioningAddress
                && accessPointConfiguration.Ssid == configuration.ProvisioningAccessPointName)
            {
                return true;
            }

            networkInterface.EnableStaticIPv4(configuration.ProvisioningAddress, SubnetMask, configuration.ProvisioningAddress);
            accessPointConfiguration.Options = WirelessAPConfiguration.ConfigurationOptions.Enable | WirelessAPConfiguration.ConfigurationOptions.AutoStart;
            accessPointConfiguration.Ssid = configuration.ProvisioningAccessPointName;
            accessPointConfiguration.MaxConnections = 1;
            accessPointConfiguration.Authentication = System.Net.NetworkInformation.AuthenticationType.WPA2;
            accessPointConfiguration.Password = configuration.ProvisioningPassword;
            accessPointConfiguration.SaveConfiguration();
            return false;
        }

        private void DisableAccessPoint()
        {
            NetworkInterface networkInterface = GetAccessPointInterface();
            if (networkInterface == null)
            {
                return;
            }

            WirelessAPConfiguration accessPointConfiguration = GetAccessPointConfiguration(networkInterface);
            if (accessPointConfiguration == null)
            {
                return;
            }

            accessPointConfiguration.Options = WirelessAPConfiguration.ConfigurationOptions.None;
            accessPointConfiguration.SaveConfiguration();
        }

        private static NetworkInterface GetAccessPointInterface()
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                if (interfaces[index].NetworkInterfaceType == NetworkInterfaceType.WirelessAP)
                {
                    return interfaces[index];
                }
            }

            return null;
        }

        private static WirelessAPConfiguration GetAccessPointConfiguration(NetworkInterface networkInterface)
        {
            return WirelessAPConfiguration.GetAllWirelessAPConfigurations()[networkInterface.SpecificConfigId];
        }

        private void RunServer()
        {
            try
            {
                _listener.Start();
                while (_running && _listener != null && _listener.IsListening)
                {
                    HttpListenerContext context = _listener.GetContext();
                    if (context != null)
                    {
                        ProcessRequest(context);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Provisioning web server skoncil: " + ex.Message);
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            _result.ClientConnected = true;

            if (request.HttpMethod == "POST")
            {
                Hashtable values = ParseForm(request.InputStream);
                StationConfiguration newConfiguration = BuildConfiguration(values);

                if (newConfiguration == null)
                {
                    WriteHtml(response, CreatePage("PIN neni platny, konfigurace nebyla ulozena."));
                    return;
                }

                _result.Configuration = newConfiguration;
                _result.ConfigurationSaved = true;
                _result.TimedOut = false;
                _result.Message = "Konfigurace byla prijata z weboveho nastaveni.";
                Debug.WriteLine("Provisioning web ulozil SSID: " + (newConfiguration.WifiSsid == string.Empty ? "<nenastaveno>" : newConfiguration.WifiSsid));
                WriteHtml(response, CreatePage("Nastaveni ulozeno. Stanice prechazi do meteo rezimu."));
                _running = false;
                return;
            }

            WriteHtml(response, CreatePage(string.Empty));
        }

        private StationConfiguration BuildConfiguration(Hashtable values)
        {
            string pin = GetValue(values, "pin");
            if (pin != _baseConfiguration.ProvisioningPassword)
            {
                return null;
            }

            StationConfiguration configuration = CloneConfiguration(_baseConfiguration);
            configuration.WifiSsid = GetValue(values, "ssid");
            configuration.WifiPassword = GetValue(values, "password");
            configuration.ApiBaseUrl = GetValue(values, "api");
            configuration.UseDhcp = GetValue(values, "dhcp") == "on";
            configuration.StaticIpAddress = GetValue(values, "ip");
            configuration.GatewayAddress = GetValue(values, "gateway");
            configuration.SubnetMask = GetValue(values, "mask");
            configuration.DnsAddress = GetValue(values, "dns");
            return configuration;
        }

        private static Hashtable ParseForm(Stream inputStream)
        {
            byte[] buffer = new byte[inputStream.Length];
            inputStream.Read(buffer, 0, (int)inputStream.Length);
            string raw = System.Text.Encoding.UTF8.GetString(buffer, 0, buffer.Length);
            Hashtable values = new Hashtable();
            string[] pairs = raw.Split('&');

            for (int index = 0; index < pairs.Length; index++)
            {
                string[] nameValue = pairs[index].Split('=');
                if (nameValue.Length == 2)
                {
                    values[UrlDecode(nameValue[0])] = UrlDecode(nameValue[1]);
                }
            }

            return values;
        }

        private static string GetValue(Hashtable values, string key)
        {
            object value = values[key];
            return value == null ? string.Empty : (string)value;
        }

        private static string UrlDecode(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string decoded = string.Empty;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '+')
                {
                    decoded += ' ';
                }
                else if (character == '%' && index + 2 < value.Length)
                {
                    int high = FromHex(value[index + 1]);
                    int low = FromHex(value[index + 2]);
                    if (high >= 0 && low >= 0)
                    {
                        decoded += (char)((high * 16) + low);
                        index += 2;
                    }
                    else
                    {
                        decoded += character;
                    }
                }
                else
                {
                    decoded += character;
                }
            }

            return decoded;
        }

        /// <summary>
        /// Converts one URL-encoded hexadecimal character to its numeric value.
        /// </summary>
        private static int FromHex(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            return -1;
        }

        private static void WriteHtml(HttpListenerResponse response, string html)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private string CreatePage(string message)
        {
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>MeteoStanice setup</title>"
                + "<style>body{font-family:Arial;margin:24px;max-width:520px}label{display:block;margin-top:12px}input,select{width:100%;padding:8px;box-sizing:border-box}button{margin-top:16px;padding:10px 14px}</style>"
                + "</head><body><h1>MeteoStanice setup</h1><form method=\"post\">"
                + SsidSelect()
                + Field("password", "Wi-Fi heslo", _baseConfiguration.WifiPassword, "password")
                + Field("api", "API URL", _baseConfiguration.ApiBaseUrl, "text")
                + "<label><input style=\"width:auto\" type=\"checkbox\" name=\"dhcp\" " + (_baseConfiguration.UseDhcp ? "checked" : "") + "> DHCP</label>"
                + Field("ip", "Staticka IP", _baseConfiguration.StaticIpAddress, "text")
                + Field("gateway", "Brana", _baseConfiguration.GatewayAddress, "text")
                + Field("mask", "Maska", _baseConfiguration.SubnetMask, "text")
                + Field("dns", "DNS", _baseConfiguration.DnsAddress, "text")
                + Field("pin", "PIN", string.Empty, "password")
                + "<button type=\"submit\">Ulozit</button></form><p>" + message + "</p></body></html>";
        }

        /// <summary>
        /// Builds Wi-Fi SSID selection from a short real adapter scan.
        /// </summary>
        private string SsidSelect()
        {
            string options = SsidOption(_baseConfiguration.WifiSsid, _baseConfiguration.WifiSsid, true);

            try
            {
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                if (adapters == null || adapters.Length == 0)
                {
                    return "<label>Wi-Fi SSID<select name=\"ssid\">" + options + "</select></label>";
                }

                WifiAdapter adapter = adapters[0];
                _scanCompleted = false;
                adapter.AvailableNetworksChanged += OnAvailableNetworksChanged;
                adapter.ScanAsync();

                int waitCount = 0;
                while (!_scanCompleted && waitCount < 12)
                {
                    Thread.Sleep(250);
                    waitCount++;
                }

                adapter.AvailableNetworksChanged -= OnAvailableNetworksChanged;
                WifiNetworkReport report = adapter.NetworkReport;
                if (report != null && report.AvailableNetworks != null)
                {
                    options = SsidOption(string.Empty, "-- vyber Wi-Fi sit --", _baseConfiguration.WifiSsid == null || _baseConfiguration.WifiSsid.Length == 0);
                    int count = report.AvailableNetworks.Length;
                    int max = count > 12 ? 12 : count;
                    for (int index = 0; index < max; index++)
                    {
                        WifiAvailableNetwork network = report.AvailableNetworks[index];
                        if (network.Ssid != null && network.Ssid.Length > 0)
                        {
                            bool selected = network.Ssid == _baseConfiguration.WifiSsid;
                            options += SsidOption(network.Ssid, network.Ssid + " (" + network.NetworkRssiInDecibelMilliwatts + " dBm)", selected);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Provisioning Wi-Fi scan selhal: " + exception.Message);
            }

            return "<label>Wi-Fi SSID<select name=\"ssid\">" + options + "</select></label>";
        }

        /// <summary>
        /// Creates one escaped SSID option.
        /// </summary>
        private static string SsidOption(string value, string label, bool selected)
        {
            return "<option value=\"" + HtmlEncode(value) + "\"" + (selected ? " selected" : string.Empty) + ">" + HtmlEncode(label) + "</option>";
        }

        private static string Field(string name, string label, string value, string type)
        {
            return "<label>" + label + "<input name=\"" + name + "\" type=\"" + type + "\" value=\"" + HtmlEncode(value) + "\"></label>";
        }

        private void OnAvailableNetworksChanged(WifiAdapter sender, object e)
        {
            _scanCompleted = true;
        }

        private static string HtmlEncode(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            value = ReplaceAll(value, "&", "&amp;");
            value = ReplaceAll(value, "\"", "&quot;");
            value = ReplaceAll(value, "<", "&lt;");
            value = ReplaceAll(value, ">", "&gt;");
            return value;
        }

        private static string ReplaceAll(string value, string oldValue, string newValue)
        {
            if (value == null || oldValue == null || oldValue.Length == 0)
            {
                return value;
            }

            string result = string.Empty;
            int currentIndex = 0;
            int foundIndex = value.IndexOf(oldValue);

            while (foundIndex >= 0)
            {
                result += value.Substring(currentIndex, foundIndex - currentIndex);
                result += newValue;
                currentIndex = foundIndex + oldValue.Length;
                foundIndex = value.IndexOf(oldValue, currentIndex);
            }

            result += value.Substring(currentIndex, value.Length - currentIndex);
            return result;
        }

        private static StationConfiguration CloneConfiguration(StationConfiguration source)
        {
            StationConfiguration configuration = new StationConfiguration();
            configuration.StationId = source.StationId;
            configuration.DeviceHardwareId = source.DeviceHardwareId;
            configuration.StationName = source.StationName;
            configuration.FirmwareVersion = source.FirmwareVersion;
            configuration.WifiSsid = source.WifiSsid;
            configuration.WifiPassword = source.WifiPassword;
            configuration.ApiBaseUrl = source.ApiBaseUrl;
            configuration.UseDhcp = source.UseDhcp;
            configuration.StaticIpAddress = source.StaticIpAddress;
            configuration.GatewayAddress = source.GatewayAddress;
            configuration.SubnetMask = source.SubnetMask;
            configuration.DnsAddress = source.DnsAddress;
            configuration.ProvisioningAccessPointName = source.ProvisioningAccessPointName;
            configuration.ProvisioningPassword = source.ProvisioningPassword;
            configuration.ProvisioningAddress = source.ProvisioningAddress;
            configuration.ProvisioningWindowMilliseconds = source.ProvisioningWindowMilliseconds;
            return configuration;
        }
    }
}
