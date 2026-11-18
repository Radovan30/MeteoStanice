using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MeteoStanice
{
    /// <summary>
    /// Uploads raw samples and device events to the backend API over plain HTTP.
    /// </summary>
    public sealed class NanoUploadClient
    {
        // Version 2 adds SPS30 number concentrations and station location text.
        private const int ProtocolVersion = 2;
        private const int FailedUploadBackoffMilliseconds = 30000;

        // True when an API base URL is configured.
        private bool _httpEnabled;
        private bool _apiConfigured;
        // Configured API base URL.
        private string _apiBaseUrl;
        // Optional API key sent with station uploads.
        private string _apiKey;
        // Current station identity used in upload payloads.
        private StationIdentity _identity;
        private IStationConfigurationStore _configurationStore;
        private StationConfiguration _configuration;
        private long _nextHttpAttemptTicks;
        // Random identifier of the current boot; a new value in the API marks a station restart.
        private string _bootId = string.Empty;
        private int _queuePendingCount;
        private int _droppedRamCount;
        private int _droppedFlashCount;
        private int _rejectedSampleCount;

        // True after at least one upload attempt happened.
        public bool LastUploadAttempted { get; private set; }
        // True when the last upload attempt was acknowledged by the server or simulator.
        public bool LastUploadSucceeded { get; private set; }
        // Last HTTP status code, or 0 when no HTTP response was received.
        public int LastHttpStatusCode { get; private set; }
        // True when the server permanently rejected the last payload (HTTP 400, 413 or 422).
        // Such a record would fail forever, so callers remove it from the queue.
        public bool LastUploadRejected { get; private set; }
        // Consecutive attempts that ended without any HTTP response (socket, DNS or timeout error).
        public int ConsecutiveNetworkFailures { get; private set; }

        /// <summary>
        /// Stores identity fields included in upload payloads.
        /// </summary>
        public void ConfigureIdentity(StationIdentity identity)
        {
            _identity = identity;
        }

        /// <summary>
        /// Stores mutable configuration so the backend-assigned station code can be persisted.
        /// </summary>
        public void ConfigureConfiguration(IStationConfigurationStore configurationStore, StationConfiguration configuration)
        {
            _configurationStore = configurationStore;
            _configuration = configuration;
        }

        /// <summary>
        /// Enables or disables real HTTP upload after server health check.
        /// </summary>
        public void ConfigureHttp(string apiBaseUrl, string apiKey, bool httpEnabled)
        {
            _apiBaseUrl = StationConfigurationService.NormalizeApiBaseUrl(apiBaseUrl);
            _apiKey = apiKey;
            _apiConfigured = _apiBaseUrl != null && _apiBaseUrl.Length > 0;
            _httpEnabled = _apiConfigured;
            if (_apiConfigured && !httpEnabled)
            {
                Debug.WriteLine("Upload HTTP bude zkouset primy endpoint i kdyz health check nebyl dostupny.");
            }

            Debug.WriteLine("Upload HTTP=" + _httpEnabled + ", API=" + (_apiBaseUrl == null || _apiBaseUrl.Length == 0 ? "<nenastaveno>" : _apiBaseUrl));
        }

        /// <summary>
        /// Uploads one device event to the simulated server.
        /// </summary>
        public bool UploadEvent(NanoDeviceEvent deviceEvent)
        {
            if (deviceEvent == null)
            {
                MarkUploadResult(false, 0);
                return false;
            }

            if (_httpEnabled)
            {
                if (!CanAttemptHttp())
                {
                    MarkUploadResult(false, 0);
                    return false;
                }

                return PostJson(BuildUrl("events"), SerializeEvent(deviceEvent), "event #" + deviceEvent.SequenceNumber);
            }

            MarkUploadResult(false, 0);
            return false;
        }

        /// <summary>
        /// Uploads one batch of current raw sensor samples.
        /// </summary>
        public bool UploadSamples(NanoRawSample[] samples, int count)
        {
            if (samples == null || count < 1)
            {
                MarkUploadResult(false, 0);
                return false;
            }

            if (_httpEnabled)
            {
                if (!CanAttemptHttp())
                {
                    MarkUploadResult(false, 0);
                    return false;
                }

                return PostJson(BuildUrl("samples"), SerializeRawSamples(samples, count), "raw samples " + count);
            }

            MarkUploadResult(false, 0);
            return false;
        }

        /// <summary>
        /// Builds one API endpoint URL from configured base URL.
        /// </summary>
        private string BuildUrl(string resource)
        {
            if (_apiBaseUrl.EndsWith("/"))
            {
                return _apiBaseUrl + resource;
            }

            return _apiBaseUrl + "/" + resource;
        }

        /// <summary>
        /// Sends one JSON payload to the API server.
        /// </summary>
        private bool PostJson(string url, string json, string label)
        {
            try
            {
                NanoHttpResponse response = RawHttpPost(url, json, _apiKey);
                bool success = response.StatusCode >= 200 && response.StatusCode <= 299;
                Debug.WriteLine("HTTP upload " + label + " status=" + response.StatusCode);
                MarkUploadResult(success, response.StatusCode);
                ConsecutiveNetworkFailures = response.StatusCode > 0 ? 0 : ConsecutiveNetworkFailures + 1;
                LastUploadRejected = response.StatusCode == 400 || response.StatusCode == 413 || response.StatusCode == 422;
                if (success)
                {
                    TryApplyServerStationIdentity(response.Body);
                    _nextHttpAttemptTicks = 0;
                }
                else
                {
                    BackoffHttpAttempts();
                }

                return success;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("HTTP upload " + label + " failed: " + exception.Message);
                MarkUploadResult(false, 0);
                ConsecutiveNetworkFailures++;
                BackoffHttpAttempts();
                return false;
            }
        }

        private static NanoHttpResponse RawHttpPost(string url, string json, string apiKey)
        {
            HttpEndpoint endpoint = ParseHttpEndpoint(url);
            byte[] body = Encoding.UTF8.GetBytes(json);
            string hostHeader = endpoint.Port == 80 ? endpoint.Host : endpoint.Host + ":" + endpoint.Port;
            string header = "POST " + endpoint.Path + " HTTP/1.1\r\n"
                + "Host: " + hostHeader + "\r\n"
                + "Connection: close\r\n"
                + "Content-Type: application/json\r\n"
                + "Content-Length: " + body.Length + "\r\n";

            if (apiKey != null && apiKey.Length > 0)
            {
                header += "X-Api-Key: " + apiKey + "\r\n";
            }

            header += "\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            Socket socket = null;

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.SendTimeout = 4000;
                socket.ReceiveTimeout = 4000;
                socket.Connect(new IPEndPoint(ResolveHost(endpoint.Host), endpoint.Port));
                SendAll(socket, headerBytes);
                SendAll(socket, body);
                return ReadHttpResponse(socket);
            }
            finally
            {
                if (socket != null)
                {
                    socket.Close();
                }
            }
        }

        private static HttpEndpoint ParseHttpEndpoint(string url)
        {
            string prefix = "http://";
            if (url == null || url.Length <= prefix.Length || url.Substring(0, prefix.Length) != prefix)
            {
                throw new System.Exception("Podporovana je jen HTTP URL.");
            }

            int hostStart = prefix.Length;
            int pathStart = url.IndexOf("/", hostStart);
            string hostPort = pathStart < 0 ? url.Substring(hostStart) : url.Substring(hostStart, pathStart - hostStart);
            string path = pathStart < 0 ? "/" : url.Substring(pathStart);
            int port = 80;
            string host = hostPort;
            int portStart = hostPort.IndexOf(":");
            if (portStart >= 0)
            {
                host = hostPort.Substring(0, portStart);
                port = ParsePositiveInt(hostPort.Substring(portStart + 1), 80);
            }

            HttpEndpoint endpoint = new HttpEndpoint();
            endpoint.Host = host;
            endpoint.Port = port;
            endpoint.Path = path;
            return endpoint;
        }

        private static IPAddress ResolveHost(string host)
        {
            try
            {
                return IPAddress.Parse(host);
            }
            catch
            {
                IPHostEntry entry = Dns.GetHostEntry(host);
                if (entry == null || entry.AddressList == null || entry.AddressList.Length == 0)
                {
                    throw new System.Exception("Host nelze prelozit.");
                }

                return entry.AddressList[0];
            }
        }

        private static NanoHttpResponse ReadHttpResponse(Socket socket)
        {
            const int MaxResponseChars = 4096;
            byte[] buffer = new byte[512];
            StringBuilder responseBuilder = new StringBuilder();

            while (responseBuilder.Length < MaxResponseChars)
            {
                int received = 0;
                try
                {
                    received = socket.Receive(buffer);
                }
                catch
                {
                    break;
                }

                if (received <= 0)
                {
                    break;
                }

                responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, received));
            }

            string response = responseBuilder.ToString();
            NanoHttpResponse result = new NanoHttpResponse();
            int firstSpace = response.IndexOf(" ");
            if (firstSpace < 0 || firstSpace + 4 > response.Length)
            {
                result.StatusCode = 0;
                result.Body = string.Empty;
                return result;
            }

            result.StatusCode = ParsePositiveInt(response.Substring(firstSpace + 1, 3), 0);
            int bodyStart = response.IndexOf("\r\n\r\n");
            result.Body = bodyStart < 0 ? string.Empty : response.Substring(bodyStart + 4);
            return result;
        }

        private static int ParsePositiveInt(string value, int defaultValue)
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
        /// Sets the identifier of the current boot sent with every sample batch.
        /// </summary>
        public void ConfigureBoot(string bootId)
        {
            _bootId = bootId == null ? string.Empty : bootId;
        }

        /// <summary>
        /// Queue state sent with the next sample batch, so the API can record data lost in the station:
        /// samples waiting for upload, samples dropped from the full RAM queue (since boot), samples
        /// overwritten in the full flash queue (persistent) and batches rejected by the server (since boot).
        /// </summary>
        public void SetQueueDiagnostics(int pendingCount, int droppedRamCount, int droppedFlashCount, int rejectedSampleCount)
        {
            _queuePendingCount = pendingCount;
            _droppedRamCount = droppedRamCount;
            _droppedFlashCount = droppedFlashCount;
            _rejectedSampleCount = rejectedSampleCount;
        }

        /// <summary>
        /// Clears the network failure counter after the Wi-Fi link was re-established.
        /// </summary>
        public void ResetNetworkFailures()
        {
            ConsecutiveNetworkFailures = 0;
        }

        private void MarkUploadResult(bool succeeded, int httpStatusCode)
        {
            LastUploadRejected = false;
            LastUploadAttempted = true;
            LastUploadSucceeded = succeeded;
            LastHttpStatusCode = httpStatusCode;
        }

        private bool CanAttemptHttp()
        {
            if (_nextHttpAttemptTicks == 0)
            {
                return true;
            }

            if (DateTime.UtcNow.Ticks < _nextHttpAttemptTicks)
            {
                return false;
            }

            _nextHttpAttemptTicks = 0;
            return true;
        }

        private void BackoffHttpAttempts()
        {
            _nextHttpAttemptTicks = DateTime.UtcNow.Ticks + (FailedUploadBackoffMilliseconds * TimeSpan.TicksPerMillisecond);
        }

        private void TryApplyServerStationIdentity(string responseBody)
        {
            if (_configurationStore == null || _configuration == null || responseBody == null || responseBody.Length == 0)
            {
                return;
            }

            string stationCode = ReadJsonStringProperty(responseBody, "stationCode");
            if (stationCode == null || stationCode.Length == 0 || !ShouldAcceptServerStationCode(_configuration.StationId))
            {
                return;
            }

            string stationName = ReadJsonStringProperty(responseBody, "stationName");
            _configuration.StationId = stationCode;
            if (stationName != null && stationName.Length > 0)
            {
                _configuration.StationName = stationName;
            }
            else if (_configuration.StationName == null || _configuration.StationName.Length == 0 || ShouldAcceptServerStationCode(_configuration.StationName))
            {
                _configuration.StationName = stationCode;
            }

            if (_identity != null)
            {
                _identity.StationId = _configuration.StationId;
                _identity.StationName = _configuration.StationName;
            }

            if (_configurationStore.Save(_configuration))
            {
                Debug.WriteLine("Station kod prevzat ze serveru a ulozen do flash: " + stationCode);
            }
        }

        private static void SendAll(Socket socket, byte[] data)
        {
            int sentTotal = 0;
            while (sentTotal < data.Length)
            {
                int sent = socket.Send(data, sentTotal, data.Length - sentTotal, SocketFlags.None);
                if (sent <= 0)
                {
                    throw new System.Exception("HTTP socket send selhal.");
                }

                sentTotal += sent;
            }
        }

        private static bool ShouldAcceptServerStationCode(string currentStationCode)
        {
            if (currentStationCode == null || currentStationCode.Length == 0)
            {
                return true;
            }

            return EqualsIgnoreCase(currentStationCode, "METEO-UNKNOWN")
                || StartsWithIgnoreCase(currentStationCode, "METEO-TEST-")
                || LooksLikeGuid(currentStationCode);
        }

        private static string ReadJsonStringProperty(string json, string propertyName)
        {
            string marker = "\"" + propertyName + "\"";
            int nameStart = json.IndexOf(marker);
            if (nameStart < 0)
            {
                return string.Empty;
            }

            int colon = json.IndexOf(":", nameStart + marker.Length);
            if (colon < 0)
            {
                return string.Empty;
            }

            int valueStart = json.IndexOf("\"", colon + 1);
            if (valueStart < 0)
            {
                return string.Empty;
            }

            StringBuilder value = new StringBuilder();
            for (int index = valueStart + 1; index < json.Length; index++)
            {
                char character = json[index];
                if (character == '"')
                {
                    return value.ToString();
                }

                if (character == '\\' && index + 1 < json.Length)
                {
                    char escaped = json[index + 1];
                    if (escaped == '"' || escaped == '\\' || escaped == '/')
                    {
                        value.Append(escaped);
                        index++;
                    }
                    else if (escaped == 'n')
                    {
                        value.Append('\n');
                        index++;
                    }
                    else if (escaped == 'r')
                    {
                        value.Append('\r');
                        index++;
                    }
                    else if (escaped == 't')
                    {
                        value.Append('\t');
                        index++;
                    }
                    else
                    {
                        value.Append(character);
                    }
                }
                else
                {
                    value.Append(character);
                }
            }

            return string.Empty;
        }

        private static bool EqualsIgnoreCase(string value, string expected)
        {
            return value != null && expected != null && value.ToLower() == expected.ToLower();
        }

        private static bool StartsWithIgnoreCase(string value, string prefix)
        {
            return value != null && prefix != null && value.Length >= prefix.Length && value.Substring(0, prefix.Length).ToLower() == prefix.ToLower();
        }

        private static bool LooksLikeGuid(string value)
        {
            return value != null
                && value.Length == 36
                && value.IndexOf("-") == 8;
        }

        /// <summary>
        /// Serializes a compact batch accepted by POST /api/samples.
        /// </summary>
        private string SerializeRawSamples(NanoRawSample[] samples, int count)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"protocolVersion\":");
            json.Append(ProtocolVersion);
            json.Append(",");
            json.Append("\"stationGuid\":\"");
            json.Append(JsonEscape(GetStationGuid()));
            json.Append("\",\"stationCode\":\"");
            json.Append(JsonEscape(GetStationId()));
            json.Append("\",\"hardwareId\":\"");
            json.Append(JsonEscape(GetHardwareId()));
            json.Append("\",\"firmwareVersion\":\"");
            json.Append(JsonEscape(GetFirmwareVersion()));
            json.Append("\",\"locationText\":\"");
            json.Append(JsonEscape(GetLocationText()));
            json.Append("\",\"bootId\":\"");
            json.Append(JsonEscape(_bootId));
            json.Append("\",\"queuePendingCount\":");
            json.Append(_queuePendingCount);
            json.Append(",\"droppedRamCount\":");
            json.Append(_droppedRamCount);
            json.Append(",\"droppedFlashCount\":");
            json.Append(_droppedFlashCount);
            json.Append(",\"rejectedSampleCount\":");
            json.Append(_rejectedSampleCount);
            json.Append(",\"samples\":[");

            for (int index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    json.Append(",");
                }

                AppendRawSample(json, samples[index]);
            }

            json.Append("]}");
            return json.ToString();
        }

        private static void AppendRawSample(StringBuilder json, NanoRawSample sample)
        {
            json.Append("{\"sequenceNumber\":");
            json.Append(sample.SequenceNumber);
            json.Append(",\"measuredAt\":\"");
            json.Append(sample.SampledAt.ToString("o"));
            json.Append("\",\"temperatureCelsius\":");
            json.Append(JsonNumber(sample.HasSht40, sample.TemperatureCelsius));
            json.Append(",\"humidityPercent\":");
            json.Append(JsonNumber(sample.HasSht40, sample.HumidityPercent));
            json.Append(",\"pressureHpa\":");
            json.Append(JsonNumber(sample.HasBmp280, sample.PressureHpa));
            json.Append(",\"bmp280TemperatureCelsius\":");
            json.Append(JsonNumber(sample.HasBmp280, sample.Bmp280TemperatureCelsius));
            json.Append(",\"pm1\":");
            json.Append(JsonNumber(sample.HasSps30, sample.Pm1));
            json.Append(",\"pm25\":");
            json.Append(JsonNumber(sample.HasSps30, sample.Pm25));
            json.Append(",\"pm4\":");
            json.Append(JsonNumber(sample.HasSps30, sample.Pm4));
            json.Append(",\"pm10\":");
            json.Append(JsonNumber(sample.HasSps30, sample.Pm10));
            json.Append(",\"numberPm05\":");
            json.Append(JsonNumber(sample.HasSps30, sample.NumberPm05));
            json.Append(",\"numberPm1\":");
            json.Append(JsonNumber(sample.HasSps30, sample.NumberPm1));
            json.Append(",\"numberPm25\":");
            json.Append(JsonNumber(sample.HasSps30, sample.NumberPm25));
            json.Append(",\"numberPm4\":");
            json.Append(JsonNumber(sample.HasSps30, sample.NumberPm4));
            json.Append(",\"numberPm10\":");
            json.Append(JsonNumber(sample.HasSps30, sample.NumberPm10));
            json.Append(",\"typicalParticleSize\":");
            json.Append(JsonNumber(sample.HasSps30, sample.TypicalParticleSize));
            json.Append(",\"batteryVoltage\":");
            json.Append(JsonNumber(sample.HasBatteryVoltage, sample.BatteryVoltage));
            json.Append(",\"batteryLow\":");
            json.Append(sample.BatteryLow ? "true" : "false");
            json.Append(",\"powerSource\":\"");
            json.Append(sample.BatteryActive ? "BAT" : "SRC");
            json.Append("\",\"sht40Status\":\"");
            json.Append(sample.HasSht40 ? "OK" : SensorErrorName(sample.Sht40Error));
            json.Append("\",\"bmp280Status\":\"");
            json.Append(sample.HasBmp280 ? "OK" : SensorErrorName(sample.Bmp280Error));
            json.Append("\",\"sps30Status\":\"");
            json.Append(sample.HasSps30 ? "OK" : SensorErrorName(sample.Sps30Error));
            json.Append("\",\"status\":\"");
            json.Append(!sample.HasSht40 || !sample.HasBmp280 || !sample.HasSps30
                ? "DEGRADED"
                : sample.BatteryLow ? "BATTERY_LOW" : "OK");
            json.Append("\"}");
        }

        /// <summary>
        /// Formats a measured value with three decimals; missing, NaN and infinite values become JSON null,
        /// because "NaN" is not valid JSON and the API would reject the whole batch.
        /// </summary>
        private static string JsonNumber(bool hasValue, double value)
        {
            if (!hasValue || double.IsNaN(value) || double.IsInfinity(value))
            {
                return "null";
            }

            return value.ToString("F3");
        }

        private static string SensorErrorName(SensorErrorCode errorCode)
        {
            if (errorCode == SensorErrorCode.NotDetected) return "NotDetected";
            if (errorCode == SensorErrorCode.Timeout) return "Timeout";
            if (errorCode == SensorErrorCode.CrcFailed) return "CrcFailed";
            if (errorCode == SensorErrorCode.InvalidResponse) return "InvalidResponse";
            if (errorCode == SensorErrorCode.CommunicationFailed) return "CommunicationFailed";
            if (errorCode == SensorErrorCode.FanError) return "FanError";
            if (errorCode == SensorErrorCode.LaserError) return "LaserError";
            if (errorCode == SensorErrorCode.DisabledOnBattery) return "DisabledOnBattery";
            return "Unknown";
        }

        /// <summary>
        /// Serializes one device event to compact JSON.
        /// </summary>
        private string SerializeEvent(NanoDeviceEvent deviceEvent)
        {
            return "{"
                + "\"protocolVersion\":" + ProtocolVersion + ","
                + "\"stationGuid\":\"" + JsonEscape(GetStationGuid()) + "\","
                + "\"eventGuid\":\"" + JsonEscape(deviceEvent.EventGuid) + "\","
                + "\"stationId\":\"" + JsonEscape(deviceEvent.StationId) + "\","
                + "\"stationCode\":\"" + JsonEscape(deviceEvent.StationId) + "\","
                + "\"hardwareId\":\"" + JsonEscape(GetHardwareId()) + "\","
                + "\"firmwareVersion\":\"" + JsonEscape(GetFirmwareVersion()) + "\","
                + "\"sequenceNumber\":" + deviceEvent.SequenceNumber + ","
                + "\"occurredAt\":\"" + JsonEscape(deviceEvent.OccurredAt.ToString("o")) + "\","
                + "\"severity\":\"" + deviceEvent.Severity.ToString() + "\","
                + "\"category\":\"" + deviceEvent.Category.ToString() + "\","
                + "\"code\":\"" + deviceEvent.Code.ToString() + "\","
                + "\"component\":\"" + JsonEscape(deviceEvent.Component) + "\","
                + "\"message\":\"" + JsonEscape(deviceEvent.Message) + "\""
                + "}";
        }

        private string GetStationGuid()
        {
            return _identity == null || _identity.StationGuid == null ? string.Empty : _identity.StationGuid;
        }

        private string GetStationId()
        {
            return _identity == null || _identity.StationId == null ? string.Empty : _identity.StationId;
        }

        private string GetHardwareId()
        {
            return _identity == null || _identity.DeviceHardwareId == null ? string.Empty : _identity.DeviceHardwareId;
        }

        private string GetFirmwareVersion()
        {
            return _identity == null || _identity.FirmwareVersion == null ? string.Empty : _identity.FirmwareVersion;
        }

        private string GetLocationText()
        {
            return _configuration == null || _configuration.LocationText == null ? string.Empty : _configuration.LocationText;
        }

        /// <summary>
        /// Escapes a value for JSON string output.
        /// </summary>
        private string JsonEscape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            StringBuilder escaped = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '\\')
                {
                    escaped.Append("\\\\");
                }
                else if (character == '"')
                {
                    escaped.Append("\\\"");
                }
                else if (character == '\r')
                {
                    escaped.Append("\\r");
                }
                else if (character == '\n')
                {
                    escaped.Append("\\n");
                }
                else
                {
                    escaped.Append(character);
                }
            }

            return escaped.ToString();
        }

    }

    internal sealed class HttpEndpoint
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Path { get; set; }
    }

    internal sealed class NanoHttpResponse
    {
        public int StatusCode { get; set; }
        public string Body { get; set; }
    }

}
