using System;
using System.Collections;
using System.Device.Wifi;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
        private const int AccessPointAddressTimeoutMilliseconds = 3000;
        private const int SocketRetryDelayMilliseconds = 1000;
        private const int HttpRequestBufferSize = 8192;

        private Socket _serverSocket;
        private Thread _serverThread;
        private DhcpServer _dhcpServer;
        private readonly IStationConfigurationStore _configurationStore;
        private readonly StationConfigurationService _configurationService;
        private StationConfiguration _baseConfiguration;
        private ProvisioningWindowResult _result;
        private bool _running;
        private bool _scanCompleted;

        /// <summary>
        /// Creates a provisioning portal that only returns submitted configuration to its caller.
        /// </summary>
        public NanoFrameworkProvisioningPortal()
        {
        }

        /// <summary>
        /// Creates a provisioning portal that saves submitted configuration immediately.
        /// </summary>
        public NanoFrameworkProvisioningPortal(IStationConfigurationStore configurationStore, StationConfigurationService configurationService)
        {
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }

        /// <summary>
        /// Starts the SoftAP, DHCP server and web server.
        /// </summary>
        public bool Start(StationConfiguration configuration)
        {
            if (_serverThread != null && _serverThread.IsAlive)
            {
                Debug.WriteLine("Provisioning predchozi web vlakno jeste bezi.");
                return false;
            }
            _baseConfiguration = CloneConfiguration(configuration);
            _result = new ProvisioningWindowResult();
            _running = true;

            try
            {
                if (!ConfigureAccessPoint(configuration))
                {
                    Debug.WriteLine("SoftAP konfigurace se nepodarila pripravit.");
                    Stop();
                    return false;
                }

                Thread.Sleep(1000);

                IPAddress provisioningAddress = IPAddress.Parse(configuration.ProvisioningAddress);
                _dhcpServer = new DhcpServer();
                _dhcpServer.CaptivePortalUrl = "http://" + configuration.ProvisioningAddress + "/";
                _dhcpServer.Gateway = provisioningAddress;
                if (!_dhcpServer.Start(provisioningAddress, IPAddress.Parse(SubnetMask)))
                {
                    Debug.WriteLine("DHCP server pro provisioning se nepodarilo spustit.");
                    Stop();
                    return false;
                }
                Debug.WriteLine("Provisioning DHCP server bezi na " + configuration.ProvisioningAddress + ".");

                _serverThread = new Thread(RunServer);
                _serverThread.Start();

                Debug.WriteLine("Provisioning portal bezi na http://" + configuration.ProvisioningAddress + "/");
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("Provisioning portal start selhal: " + exception.Message);
                Stop();
                return false;
            }
        }

        /// <summary>
        /// Disables saved SoftAP autostart when the station runs in meteo mode.
        /// </summary>
        public static void DisableSavedAccessPoint()
        {
            DisableAccessPointConfiguration();
        }

        /// <summary>
        /// Waits until settings are saved or the setup window expires.
        /// </summary>
        public ProvisioningWindowResult WaitForResult(int timeoutMilliseconds)
        {
            int elapsed = 0;
            int nextLog = 0;
            while (elapsed < timeoutMilliseconds)
            {
                if (_result != null && _result.ConfigurationSaved)
                {
                    return _result;
                }

                if (elapsed >= nextLog)
                {
                    Debug.WriteLine("Provisioning portal ceka, zbyva ms: " + (timeoutMilliseconds - elapsed));
                    nextLog += 5000;
                }

                Thread.Sleep(250);
                elapsed += 250;
            }

            _running = false;
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
        /// Waits until settings are saved or the setup button is pressed again.
        /// </summary>
        public ProvisioningWindowResult WaitForResultUntilCancelled(NanoTtgoButtons buttons)
        {
            int elapsed = 0;
            int nextLog = 0;
            bool setupButtonReleased = buttons == null || !buttons.IsSetupPressed();

            while (true)
            {
                bool setupButtonPressed = buttons != null && buttons.IsSetupPressed();
                if (!setupButtonReleased)
                {
                    setupButtonReleased = !setupButtonPressed;
                }
                else if (setupButtonPressed)
                {
                    Thread.Sleep(40);
                    if (!buttons.IsSetupPressed())
                    {
                        continue;
                    }

                    _running = false;
                    if (_result == null)
                    {
                        _result = new ProvisioningWindowResult();
                    }

                    _result.CancelledByButton = true;
                    _result.TimedOut = false;
                    if (_result.Configuration == null)
                    {
                        _result.Configuration = _baseConfiguration;
                    }

                    _result.Message = "Setup rezim ukoncen tlacitkem.";
                    Debug.WriteLine("Provisioning portal ukoncen tlacitkem.");
                    return _result;
                }

                if (elapsed >= nextLog)
                {
                    Debug.WriteLine("Provisioning portal ceka na tlacitko pro navrat, saved=" + (_result != null && _result.ConfigurationSaved) + ", elapsed ms: " + elapsed);
                    nextLog += 5000;
                }

                Thread.Sleep(250);
                elapsed += 250;
            }
        }

        /// <summary>
        /// Stops the web listener and disables SoftAP mode.
        /// </summary>
        public void Stop()
        {
            _running = false;

            if (_serverSocket != null)
            {
                try
                {
                    _serverSocket.Close();
                }
                catch (System.Exception exception)
                {
                    Debug.WriteLine("Provisioning web socket stop selhal: " + exception.Message);
                }
                finally
                {
                    _serverSocket = null;
                }
            }

            if (_dhcpServer != null)
            {
                try
                {
                    _dhcpServer.Stop();
                    _dhcpServer.Dispose();
                }
                catch (System.Exception exception)
                {
                    Debug.WriteLine("Provisioning DHCP stop selhal: " + exception.Message);
                }
                finally
                {
                    _dhcpServer = null;
                }
            }

            try
            {
                DisableAccessPointConfiguration();
            }
            finally
            {
                if (_serverThread != null && _serverThread.IsAlive)
                {
                    if (!_serverThread.Join(5000))
                    {
                        Debug.WriteLine("Provisioning web vlakno se dosud neukoncilo.");
                    }
                }
            }
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

            networkInterface.EnableStaticIPv4(configuration.ProvisioningAddress, SubnetMask, configuration.ProvisioningAddress);
            accessPointConfiguration.Options = WirelessAPConfiguration.ConfigurationOptions.Enable;
            accessPointConfiguration.Ssid = configuration.ProvisioningAccessPointName;
            accessPointConfiguration.MaxConnections = 1;
            accessPointConfiguration.Authentication = System.Net.NetworkInformation.AuthenticationType.WPA2;
            accessPointConfiguration.Password = configuration.ProvisioningPassword;
            accessPointConfiguration.SaveConfiguration();
            Debug.WriteLine("SoftAP konfigurace ulozena a povolena pro okamzity setup bez AutoStart, password length="
                + (configuration.ProvisioningPassword == null ? 0 : configuration.ProvisioningPassword.Length) + ".");
            return EnsureAccessPointAddress(configuration.ProvisioningAddress);
        }

        private static bool EnsureAccessPointAddress(string provisioningAddress)
        {
            int elapsed = 0;
            while (elapsed < AccessPointAddressTimeoutMilliseconds)
            {
                NetworkInterface networkInterface = GetAccessPointInterface();
                if (networkInterface != null)
                {
                    if (networkInterface.IPv4Address != provisioningAddress)
                    {
                        networkInterface.EnableStaticIPv4(provisioningAddress, SubnetMask, provisioningAddress);
                    }

                    networkInterface = GetAccessPointInterface();
                    if (networkInterface != null && networkInterface.IPv4Address == provisioningAddress)
                    {
                        Debug.WriteLine("SoftAP pripraven, IP=" + networkInterface.IPv4Address + ".");
                        return true;
                    }
                }

                Thread.Sleep(100);
                elapsed += 100;
            }

            Debug.WriteLine("SoftAP nema ocekavanou IP " + provisioningAddress + ".");
            return false;
        }

        private static void DisableAccessPointConfiguration()
        {
            NetworkInterface networkInterface = GetAccessPointInterface();
            if (networkInterface == null)
            {
                Debug.WriteLine("SoftAP vypnuti preskoceno: WirelessAP interface nebyl nalezen.");
                return;
            }

            WirelessAPConfiguration accessPointConfiguration = GetAccessPointConfiguration(networkInterface);
            if (accessPointConfiguration == null)
            {
                Debug.WriteLine("SoftAP vypnuti preskoceno: konfigurace nebyla nalezena.");
                return;
            }

            if (accessPointConfiguration.Options == WirelessAPConfiguration.ConfigurationOptions.Disable)
            {
                Debug.WriteLine("SoftAP uz je vypnuty.");
                return;
            }

            accessPointConfiguration.Options = WirelessAPConfiguration.ConfigurationOptions.Disable;
            accessPointConfiguration.SaveConfiguration();
            Debug.WriteLine("SoftAP explicitne vypnut.");
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
            int failures = 0;
            while (_running)
            {
                Socket listener = null;
                try
                {
                    listener = OpenServerSocket();
                    if (listener == null)
                    {
                        Thread.Sleep(SocketRetryDelayMilliseconds);
                        continue;
                    }

                    Debug.WriteLine("Provisioning raw web server posloucha na portu 80.");
                    failures = 0;
                    while (_running)
                    {
                        Socket client = listener.Accept();
                        if (client != null)
                        {
                            Debug.WriteLine("Provisioning TCP klient pripojen.");
                            try
                            {
                                ProcessRequest(client);
                            }
                            finally
                            {
                                try { client.Close(); } catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_running) break;
                    SocketException socketError = ex as SocketException;
                    Debug.WriteLine("Provisioning web socket chyba: " + ex.Message
                        + (socketError == null ? string.Empty : ", code=" + socketError.ErrorCode));
                    if (++failures >= 3)
                    {
                        Debug.WriteLine("Provisioning web socket stale neni pripraven, zkusim ho znovu otevrit.");
                        failures = 0;
                    }
                    Thread.Sleep(SocketRetryDelayMilliseconds);
                }
                finally
                {
                    if (listener != null)
                    {
                        try { listener.Close(); } catch { }
                        if (_serverSocket == listener)
                        {
                            _serverSocket = null;
                        }
                    }
                }
            }
            Debug.WriteLine("Provisioning web vlakno ukonceno.");
        }

        private Socket OpenServerSocket()
        {
            try
            {
                Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Any, 80));
                listener.Listen(2);
                _serverSocket = listener;
                Debug.WriteLine("Provisioning raw web socket pripraven na portu 80.");
                return listener;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Provisioning web socket nelze otevrit: " + exception.Message);
                return null;
            }
        }

        private void ProcessRequest(Socket client)
        {
            string stage = "start";
            try
            {
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                stage = "read";
                string rawRequest = ReadHttpRequest(client);
                if (rawRequest.Length == 0)
                {
                    client.Close();
                    return;
                }

                Debug.WriteLine("Provisioning HTTP chars: " + rawRequest.Length);
                stage = "route";
                string requestLine = GetRequestLine(rawRequest);
                string method = GetRequestMethod(requestLine);
                string path = GetRequestPath(requestLine);
                _result.ClientConnected = true;
                Debug.WriteLine("Provisioning HTTP request: " + method + " " + path);

                if (path == "/ping")
                {
                    WriteText(client, "OK");
                    return;
                }

                if (path == "/scan")
                {
                    WriteHtml(client, CreateScanPage());
                    return;
                }

                if (method == "POST")
                {
                    stage = "parse";
                    Hashtable values = ParseForm(GetRequestBody(rawRequest));
                    Debug.WriteLine("Provisioning POST keys: ssid=" + HasValue(values, "ssid")
                        + ", password=" + HasValue(values, "password")
                        + ", api=" + HasValue(values, "api")
                        + ", dhcp=" + HasValue(values, "dhcp")
                        + ", locationText=" + HasValue(values, "locationText"));
                    stage = "build";
                    StationConfiguration newConfiguration = BuildConfiguration(values);

                    if (newConfiguration == null)
                    {
                        stage = "response-invalid";
                        WriteHtml(client, CreateResultPage(false, "CHYBA", "Wi-Fi SSID nebo Wi-Fi heslo neni vyplnene. Nastaveni nebylo ulozeno."));
                        return;
                    }

                    stage = "save";
                    _result.Configuration = newConfiguration;
                    _result.ConfigurationSaved = SaveSubmittedConfiguration(newConfiguration);
                    _result.TimedOut = false;
                    _result.Message = _result.ConfigurationSaved ? "Konfigurace byla ulozena z weboveho nastaveni." : "Konfiguraci se nepodarilo ulozit.";
                    if (_result.ConfigurationSaved)
                    {
                        _baseConfiguration = CloneConfiguration(newConfiguration);
                    }

                    Debug.WriteLine("Provisioning web ulozil SSID: " + (newConfiguration.WifiSsid == string.Empty ? "<nenastaveno>" : newConfiguration.WifiSsid) + ", saved=" + _result.ConfigurationSaved);
                    stage = "response-saved";
                    WriteHtml(client, CreateResultPage(
                        _result.ConfigurationSaved,
                        _result.ConfigurationSaved ? "ULOZENO" : "CHYBA",
                        _result.ConfigurationSaved ? "Nastaveni bylo ulozeno. Pro navrat do meteo rezimu stiskni tlacitko na desce." : "Nastaveni se nepodarilo ulozit do flash pameti."));
                    return;
                }

                if (path.IndexOf("/?ssid=") == 0)
                {
                    _baseConfiguration.WifiSsid = UrlDecode(path.Substring(7, path.Length - 7));
                    WriteHtml(client, CreatePage("Wi-Fi SSID predvyplneno ze seznamu."));
                    return;
                }

                if (method == "GET" || method.Length == 0)
                {
                    WriteHtml(client, CreatePage(string.Empty));
                    return;
                }

                WriteText(client, "MeteoStanice provisioning nepodporovana metoda.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Provisioning HTTP request selhal ve fazi " + stage + ": " + exception.GetType().FullName + " " + exception.Message);
                try
                {
                    WriteText(client, "MeteoStanice provisioning chyba requestu: " + stage + " " + exception.GetType().Name);
                }
                catch
                {
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string ReadHttpRequest(Socket client)
        {
            byte[] buffer = new byte[HttpRequestBufferSize];
            int totalLength = 0;
            int expectedBodyLength = -1;

            while (totalLength < buffer.Length)
            {
                int received = client.Receive(buffer, totalLength, buffer.Length - totalLength, SocketFlags.None);
                if (received <= 0)
                {
                    break;
                }

                totalLength += received;
                string partialRequest = System.Text.Encoding.UTF8.GetString(buffer, 0, totalLength);
                int bodyStart = partialRequest.IndexOf("\r\n\r\n");
                if (bodyStart < 0)
                {
                    continue;
                }

                if (expectedBodyLength < 0)
                {
                    expectedBodyLength = GetContentLength(partialRequest);
                }

                int currentBodyLength = totalLength - bodyStart - 4;
                if (expectedBodyLength <= 0 || currentBodyLength >= expectedBodyLength)
                {
                    return partialRequest;
                }
            }

            if (totalLength == 0)
            {
                return string.Empty;
            }

            Debug.WriteLine("Provisioning HTTP request precten castecne, bytes=" + totalLength + "/" + buffer.Length
                + ", expected body=" + expectedBodyLength + ".");
            return System.Text.Encoding.UTF8.GetString(buffer, 0, totalLength);
        }

        private static int GetContentLength(string request)
        {
            string marker = "Content-Length:";
            int start = request.IndexOf(marker);
            if (start < 0)
            {
                return 0;
            }

            start += marker.Length;
            while (start < request.Length && request[start] == ' ')
            {
                start++;
            }

            int end = request.IndexOf("\r\n", start);
            if (end < 0)
            {
                return 0;
            }

            int length = 0;
            for (int index = start; index < end; index++)
            {
                char character = request[index];
                if (character < '0' || character > '9')
                {
                    break;
                }

                length = (length * 10) + (character - '0');
            }

            return length;
        }

        private StationConfiguration BuildConfiguration(Hashtable values)
        {
            StationConfiguration configuration = CloneConfiguration(_baseConfiguration);
            configuration.WifiSsid = GetValue(values, "ssid");
            configuration.WifiPassword = GetValue(values, "password");
            configuration.ApiBaseUrl = StationConfigurationService.NormalizeApiBaseUrl(GetValue(values, "api"));
            configuration.ApiKey = GetValue(values, "apiKey");

            if (configuration.WifiSsid == null || configuration.WifiSsid.Length == 0)
            {
                Debug.WriteLine("Provisioning form odmitnut: SSID je prazdne.");
                return null;
            }

            if (configuration.WifiPassword == null || configuration.WifiPassword.Length == 0)
            {
                Debug.WriteLine("Provisioning form odmitnut: Wi-Fi heslo je prazdne.");
                return null;
            }

            configuration.UseDhcp = GetValue(values, "dhcp") == "on";
            configuration.StaticIpAddress = GetValue(values, "ip");
            configuration.GatewayAddress = GetValue(values, "gateway");
            configuration.SubnetMask = GetValue(values, "mask");
            configuration.DnsAddress = GetValue(values, "dns");
            configuration.MeasurementIntervalMinutes = ParsePositiveInt(GetValue(values, "measurement"), 10);
            configuration.SamplePeriodSeconds = ParsePositiveInt(GetValue(values, "sample"), 1);
            configuration.MeasurementUploadsPerCycle = ParsePositiveInt(GetValue(values, "measurementUploads"), 2);
            configuration.EventUploadsPerCycle = ParsePositiveInt(GetValue(values, "eventUploads"), 4);
            configuration.LocationText = GetValue(values, "locationText");
            Debug.WriteLine("Provisioning form SSID length=" + configuration.WifiSsid.Length
                + ", password length=" + configuration.WifiPassword.Length
                + ", DHCP=" + configuration.UseDhcp
                + ", measurement min=" + configuration.MeasurementIntervalMinutes
                + ", sample s=" + configuration.SamplePeriodSeconds
                + ", location text=" + configuration.LocationText);
            return configuration;
        }

        private static int ParsePositiveInt(string value, int defaultValue)
        {
            try
            {
                int parsed = int.Parse(value);
                return parsed < 1 ? defaultValue : parsed;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static Hashtable ParseForm(string raw)
        {
            Hashtable values = new Hashtable();
            if (raw == null || raw.Length == 0)
            {
                return values;
            }

            string[] pairs = raw.Split('&');

            for (int index = 0; index < pairs.Length; index++)
            {
                int separator = pairs[index].IndexOf("=");
                if (separator > 0)
                {
                    string name = pairs[index].Substring(0, separator);
                    string value = pairs[index].Substring(separator + 1, pairs[index].Length - separator - 1);
                    values[UrlDecode(name)] = UrlDecode(value);
                }
            }

            return values;
        }

        private static string GetValue(Hashtable values, string key)
        {
            object value = values[key];
            return value == null ? string.Empty : (string)value;
        }

        private static bool HasValue(Hashtable values, string key)
        {
            object value = values[key];
            return value != null && ((string)value).Length > 0;
        }

        private static string UrlDecode(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            byte[] bytes = new byte[value.Length];
            int byteCount = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '+')
                {
                    bytes[byteCount++] = (byte)' ';
                }
                else if (character == '%' && index + 2 < value.Length)
                {
                    int high = FromHex(value[index + 1]);
                    int low = FromHex(value[index + 2]);
                    if (high >= 0 && low >= 0)
                    {
                        bytes[byteCount++] = (byte)((high * 16) + low);
                        index += 2;
                    }
                    else
                    {
                        bytes[byteCount++] = (byte)character;
                    }
                }
                else
                {
                    bytes[byteCount++] = (byte)character;
                }
            }

            return System.Text.Encoding.UTF8.GetString(bytes, 0, byteCount);
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

        private static void WriteHtml(Socket client, string html)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(html);
            WriteResponse(client, "text/html; charset=utf-8", bytes);
        }

        /// <summary>
        /// Writes a small plain-text response for connectivity diagnostics.
        /// </summary>
        private static void WriteText(Socket client, string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            WriteResponse(client, "text/plain; charset=utf-8", bytes);
        }

        /// <summary>
        /// Writes a complete HTTP response and closes the client socket.
        /// </summary>
        private static void WriteResponse(Socket client, string contentType, byte[] body)
        {
            string header = "HTTP/1.1 200 OK\r\n"
                + "Content-Type: " + contentType + "\r\n"
                + "Content-Length: " + body.Length + "\r\n"
                + "Connection: close\r\n"
                + "Cache-Control: no-store\r\n"
                + "\r\n";
            byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
            SendAll(client, headerBytes);
            SendAll(client, body);
            client.Close();
        }

        private static void SendAll(Socket client, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int sent = client.Send(bytes, offset, bytes.Length - offset, SocketFlags.None);
                if (sent <= 0) throw new Exception("HTTP spojeni uzavreno pri odesilani.");
                offset += sent;
            }
        }

        /// <summary>
        /// Reads the first HTTP request line using only ASCII characters.
        /// </summary>
        private static string GetRequestLine(string request)
        {
            int end = request.IndexOf("\r\n");
            if (end < 0)
            {
                end = request.IndexOf("\n");
            }

            if (end < 0)
            {
                end = request.Length > 128 ? 128 : request.Length;
            }

            return request.Substring(0, end);
        }

        /// <summary>
        /// Reads HTTP method from the first request line.
        /// </summary>
        private static string GetRequestMethod(string requestLine)
        {
            int end = requestLine.IndexOf(" ");
            return end < 0 ? string.Empty : requestLine.Substring(0, end);
        }

        /// <summary>
        /// Reads HTTP path from the first request line.
        /// </summary>
        private static string GetRequestPath(string requestLine)
        {
            int methodEnd = requestLine.IndexOf(" ");
            if (methodEnd < 0)
            {
                return "/";
            }

            int pathEnd = requestLine.IndexOf(" ", methodEnd + 1);
            if (pathEnd < 0)
            {
                return "/";
            }

            return requestLine.Substring(methodEnd + 1, pathEnd - methodEnd - 1);
        }

        /// <summary>
        /// Reads the body after the HTTP header separator.
        /// </summary>
        private static string GetRequestBody(string request)
        {
            int bodyStart = request.IndexOf("\r\n\r\n");
            if (bodyStart < 0)
            {
                return string.Empty;
            }

            return request.Substring(bodyStart + 4, request.Length - bodyStart - 4);
        }

        private string CreatePage(string message)
        {
            return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
                + "<title>MeteoStanice setup</title>"
                + "<style>body{font-family:Arial,sans-serif;margin:0;background:#10151d;color:#edf2f7}.wrap{max-width:440px;margin:0 auto;padding:18px}"
                + "h1{font-size:24px;margin:4px 0 2px}.sub{color:#9fb0c4;margin:0 0 16px}.box{border:1px solid #2d3b4c;border-radius:8px;padding:14px;background:#151d28}"
                + "label{display:block;margin-top:10px;font-size:13px;color:#b8c4d4}input{width:100%;margin-top:4px;padding:9px;border:1px solid #344456;border-radius:6px;background:#0d131b;color:#fff;box-sizing:border-box}"
                + ".row{display:flex;align-items:center;gap:8px;margin-top:10px}.row input{width:auto;margin:0}.btn,a.btn{display:inline-block;margin-top:14px;padding:10px 12px;border-radius:6px;background:#1f8cff;color:white;text-decoration:none;border:0}"
                + ".msg{margin-top:12px;color:#ffd166}.hint{font-size:12px;color:#90a0b4;margin-top:12px}.current{font-size:12px;color:#cbd6e2;line-height:1.5;margin:10px 0 4px}</style>"
                + "</head><body><div class=\"wrap\"><h1>MeteoStanice</h1><p class=\"sub\">Nastaveni Wi-Fi a serveru</p><div class=\"box\"><form method=\"post\" action=\"/\">"
                + "<div class=\"current\">Aktualne: SSID " + HtmlEncode(DisplayValue(_baseConfiguration.WifiSsid)) + "<br>Heslo " + PasswordState(_baseConfiguration.WifiPassword) + "<br>API " + HtmlEncode(DisplayValue(_baseConfiguration.ApiBaseUrl)) + "<br>API klic " + PasswordState(_baseConfiguration.ApiKey) + "<br>DHCP " + (_baseConfiguration.UseDhcp ? "zapnuto" : "vypnuto") + "<br>Mereni " + _baseConfiguration.MeasurementIntervalMinutes + " min, vzorek " + _baseConfiguration.SamplePeriodSeconds + " s<br>Lokace " + HtmlEncode(DisplayValue(_baseConfiguration.LocationText)) + "</div>"
                + "<a class=\"btn\" href=\"/scan\">Vybrat Wi-Fi</a>"
                + Field("ssid", "Wi-Fi SSID", _baseConfiguration.WifiSsid, "text")
                + Field("password", "Wi-Fi heslo", _baseConfiguration.WifiPassword, "password")
                + Field("api", "API URL", _baseConfiguration.ApiBaseUrl, "text")
                + Field("apiKey", "API klic", _baseConfiguration.ApiKey, "password")
                + "<label class=\"row\"><input type=\"checkbox\" name=\"dhcp\" " + (_baseConfiguration.UseDhcp ? "checked" : "") + "> DHCP</label>"
                + Field("ip", "Staticka IP", _baseConfiguration.StaticIpAddress, "text")
                + Field("gateway", "Brana", _baseConfiguration.GatewayAddress, "text")
                + Field("mask", "Maska", _baseConfiguration.SubnetMask, "text")
                + Field("dns", "DNS", _baseConfiguration.DnsAddress, "text")
                + Field("measurement", "Interval mereni v minutach", _baseConfiguration.MeasurementIntervalMinutes.ToString(), "number")
                + Field("sample", "Perioda vzorku v sekundach", _baseConfiguration.SamplePeriodSeconds.ToString(), "number")
                + Field("measurementUploads", "Upload mereni na cyklus", _baseConfiguration.MeasurementUploadsPerCycle.ToString(), "number")
                + Field("eventUploads", "Upload udalosti na cyklus", _baseConfiguration.EventUploadsPerCycle.ToString(), "number")
                + Field("locationText", "Misto meteostanice", _baseConfiguration.LocationText, "text")
                + "<button class=\"btn\" type=\"submit\">Ulozit</button></form><p class=\"msg\">" + message + "</p>"
                + "<p class=\"hint\">Po ulozeni zustane setup zapnuty. Do meteo rezimu se vrat tlacitkem.</p></div></div></body></html>";
        }

        private string CreateResultPage(bool saved, string title, string message)
        {
            string color = saved ? "#74d680" : "#ff7b72";
            return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
                + "<title>MeteoStanice setup</title>"
                + "<style>body{font-family:Arial,sans-serif;margin:0;background:#10151d;color:#edf2f7}.wrap{max-width:440px;margin:0 auto;padding:18px}"
                + ".box{border:1px solid #2d3b4c;border-radius:8px;padding:16px;background:#151d28}.status{font-size:26px;font-weight:bold;color:" + color + ";margin:0 0 8px}"
                + "p{line-height:1.45}.btn{display:inline-block;margin-top:12px;padding:10px 12px;border-radius:6px;background:#1f8cff;color:white;text-decoration:none}</style>"
                + "</head><body><div class=\"wrap\"><div class=\"box\"><div class=\"status\">" + title + "</div><p>" + HtmlEncode(message) + "</p>"
                + "<a class=\"btn\" href=\"/\">Zpet do nastaveni</a></div></div></body></html>";
        }

        /// <summary>
        /// Builds a small separate Wi-Fi scan page to keep the setup form within ESP32 memory limits.
        /// </summary>
        private string CreateScanPage()
        {
            string body = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Wi-Fi scan</title>"
                + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
                + "<style>body{font-family:Arial,sans-serif;margin:0;background:#10151d;color:#edf2f7}.wrap{max-width:440px;margin:0 auto;padding:18px}"
                + "h1{font-size:24px}.net{display:block;margin:8px 0;padding:10px;border:1px solid #2d3b4c;border-radius:6px;color:white;text-decoration:none;background:#151d28}"
                + ".back{color:#8ec5ff}</style>"
                + "</head><body><div class=\"wrap\"><h1>Vyber Wi-Fi</h1>";

            try
            {
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                if (adapters == null || adapters.Length == 0)
                {
                    return body + "<p>Wi-Fi adapter nebyl nalezen.</p><p><a class=\"back\" href=\"/\">Zpet</a></p></div></body></html>";
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
                    int count = report.AvailableNetworks.Length;
                    int max = count > 8 ? 8 : count;
                    for (int index = 0; index < max; index++)
                    {
                        WifiAvailableNetwork network = report.AvailableNetworks[index];
                        if (network.Ssid != null && network.Ssid.Length > 0)
                        {
                            body += "<a class=\"net\" href=\"/?ssid=" + UrlEncode(network.Ssid) + "\">" + HtmlEncode(network.Ssid) + "</a>";
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Provisioning Wi-Fi scan selhal: " + exception.Message);
                body += "<p>Wi-Fi scan selhal.</p>";
            }

            return body + "<p><a class=\"back\" href=\"/\">Zpet</a></p></div></body></html>";
        }

        private static string Field(string name, string label, string value, string type)
        {
            return "<label>" + label + "<input name=\"" + name + "\" type=\"" + type + "\" value=\"" + HtmlEncode(value) + "\"></label>";
        }

        private static string DisplayValue(string value)
        {
            return value == null || value.Length == 0 ? "<nenastaveno>" : value;
        }

        private static string PasswordState(string value)
        {
            return value == null || value.Length == 0 ? "<nenastaveno>" : "ulozeno";
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

        /// <summary>
        /// Encodes SSID text for a simple provisioning link query string.
        /// </summary>
        private static string UrlEncode(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string encoded = string.Empty;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if ((character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '-' || character == '_' || character == '.')
                {
                    encoded += character;
                }
                else if (character == ' ')
                {
                    encoded += "+";
                }
                else
                {
                    encoded += "%" + ToHex((character >> 4) & 0x0F) + ToHex(character & 0x0F);
                }
            }

            return encoded;
        }

        /// <summary>
        /// Converts a four-bit value to an uppercase hexadecimal character.
        /// </summary>
        private static char ToHex(int value)
        {
            return value < 10 ? (char)('0' + value) : (char)('A' + value - 10);
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
            configuration.StationGuid = source.StationGuid;
            configuration.DeviceHardwareId = source.DeviceHardwareId;
            configuration.StationName = source.StationName;
            configuration.FirmwareVersion = source.FirmwareVersion;
            configuration.WifiSsid = source.WifiSsid;
            configuration.WifiPassword = source.WifiPassword;
            configuration.ApiBaseUrl = source.ApiBaseUrl;
            configuration.ApiKey = source.ApiKey;
            configuration.UseDhcp = source.UseDhcp;
            configuration.StaticIpAddress = source.StaticIpAddress;
            configuration.GatewayAddress = source.GatewayAddress;
            configuration.SubnetMask = source.SubnetMask;
            configuration.DnsAddress = source.DnsAddress;
            configuration.MeasurementIntervalMinutes = source.MeasurementIntervalMinutes;
            configuration.SamplePeriodSeconds = source.SamplePeriodSeconds;
            configuration.MeasurementUploadsPerCycle = source.MeasurementUploadsPerCycle;
            configuration.EventUploadsPerCycle = source.EventUploadsPerCycle;
            configuration.LocationText = source.LocationText;
            configuration.ProvisioningAccessPointName = source.ProvisioningAccessPointName;
            configuration.ProvisioningPassword = source.ProvisioningPassword;
            configuration.ProvisioningAddress = source.ProvisioningAddress;
            configuration.ProvisioningHostName = source.ProvisioningHostName;
            configuration.ProvisioningWindowMilliseconds = source.ProvisioningWindowMilliseconds;
            configuration.ProvisioningRequestedOnNextBoot = source.ProvisioningRequestedOnNextBoot;
            return configuration;
        }

        private bool SaveSubmittedConfiguration(StationConfiguration configuration)
        {
            if (_configurationStore == null || _configurationService == null)
            {
                return true;
            }

            if (!_configurationService.Validate(configuration))
            {
                Debug.WriteLine("Provisioning save skipped: konfigurace neni platna.");
                return false;
            }

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

            bool verified = SameValue(configuration.WifiSsid, verifyConfiguration.WifiSsid)
                && SameValue(configuration.WifiPassword, verifyConfiguration.WifiPassword)
                && SameValue(configuration.ApiBaseUrl, verifyConfiguration.ApiBaseUrl)
                && SameValue(configuration.ApiKey, verifyConfiguration.ApiKey)
                && SameValue(configuration.LocationText, verifyConfiguration.LocationText);
            Debug.WriteLine("Provisioning save verify SSID=" + (verifyConfiguration.WifiSsid == string.Empty ? "<nenastaveno>" : verifyConfiguration.WifiSsid)
                + ", location=" + (verifyConfiguration.LocationText == string.Empty ? "<nenastaveno>" : verifyConfiguration.LocationText)
                + ", password length saved=" + (configuration.WifiPassword == null ? 0 : configuration.WifiPassword.Length)
                + ", loaded=" + (verifyConfiguration.WifiPassword == null ? 0 : verifyConfiguration.WifiPassword.Length)
                + ", ok=" + verified);
            return verified;
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
    }
}
