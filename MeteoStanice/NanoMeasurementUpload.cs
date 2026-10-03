using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MeteoStanice
{
    /// <summary>
    /// Uploads data to API server when available and otherwise keeps debug simulation.
    /// </summary>
    public sealed class NanoUploadClient
    {
        private const int FailedUploadBackoffMilliseconds = 30000;

        // True when simulated server accepts records.
        public bool ServerAvailable { get; set; }
        // True when HTTP upload is enabled after API health check.
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

        // True after at least one upload attempt happened.
        public bool LastUploadAttempted { get; private set; }
        // True when the last upload attempt was acknowledged by the server or simulator.
        public bool LastUploadSucceeded { get; private set; }
        // Last HTTP status code, or 0 when no HTTP response was received.
        public int LastHttpStatusCode { get; private set; }

        /// <summary>
        /// Creates an upload client with simulated server available.
        /// </summary>
        public NanoUploadClient()
        {
            ServerAvailable = true;
        }

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
        /// Uploads one measurement record to the simulated server.
        /// </summary>
        public bool Upload(NanoMeasurementRecord record)
        {
            if (!ServerAvailable || record == null)
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

                return PostJson(BuildUrl("measurements"), SerializeMeasurement(record), "record #" + record.SequenceNumber);
            }

            MarkUploadResult(false, 0);
            return false;
        }

        /// <summary>
        /// Uploads one device event to the simulated server.
        /// </summary>
        public bool UploadEvent(NanoDeviceEvent deviceEvent)
        {
            if (!ServerAvailable || deviceEvent == null)
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

        private void MarkUploadResult(bool succeeded, int httpStatusCode)
        {
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
        /// Serializes one measurement record to compact JSON.
        /// </summary>
        private string SerializeMeasurement(NanoMeasurementRecord record)
        {
            return "{"
                + "\"stationGuid\":\"" + JsonEscape(GetStationGuid()) + "\","
                + "\"stationId\":\"" + JsonEscape(record.StationId) + "\","
                + "\"stationCode\":\"" + JsonEscape(record.StationId) + "\","
                + "\"hardwareId\":\"" + JsonEscape(GetHardwareId()) + "\","
                + "\"firmwareVersion\":\"" + JsonEscape(GetFirmwareVersion()) + "\","
                + "\"sequenceNumber\":" + record.SequenceNumber + ","
                + "\"measuredAt\":\"" + JsonEscape(record.SampledAt.ToString("o")) + "\","
                + "\"sampledAt\":\"" + JsonEscape(record.SampledAt.ToString("o")) + "\","
                + "\"temperatureCelsius\":" + record.TemperatureCelsius + ","
                + "\"humidityPercent\":" + record.RelativeHumidityPercent + ","
                + "\"relativeHumidityPercent\":" + record.RelativeHumidityPercent + ","
                + "\"pressureHpa\":" + record.PressureHpa + ","
                + "\"pm1\":" + record.Pm1 + ","
                + "\"pm25\":" + record.Pm25 + ","
                + "\"pm4\":" + record.Pm4 + ","
                + "\"pm10\":" + record.Pm10 + ","
                + "\"numberPm05\":" + record.NumberPm05 + ","
                + "\"numberPm1\":" + record.NumberPm1 + ","
                + "\"numberPm25\":" + record.NumberPm25 + ","
                + "\"numberPm4\":" + record.NumberPm4 + ","
                + "\"numberPm10\":" + record.NumberPm10 + ","
                + "\"typicalParticleSize\":" + record.TypicalParticleSize + ","
                + "\"validValueCount\":" + record.ValidValueCount + ","
                + "\"failedReadCount\":" + record.FailedReadCount + ","
                + "\"expectedSampleCount\":" + record.ExpectedSampleCount + ","
                + "\"receivedSampleCount\":" + record.ReceivedSampleCount + ","
                + "\"sht40OkCount\":" + record.Sht40OkCount + ","
                + "\"sht40ErrorCount\":" + record.Sht40ErrorCount + ","
                + "\"bmp280OkCount\":" + record.Bmp280OkCount + ","
                + "\"bmp280ErrorCount\":" + record.Bmp280ErrorCount + ","
                + "\"sps30OkCount\":" + record.Sps30OkCount + ","
                + "\"sps30WaitCount\":" + record.Sps30WaitCount + ","
                + "\"sps30ErrorCount\":" + record.Sps30ErrorCount + ","
                + "\"sps30BatterySkippedCount\":" + record.Sps30BatterySkippedCount + ","
                + "\"temperatureMin\":" + record.TemperatureMin + ","
                + "\"temperatureMax\":" + record.TemperatureMax + ","
                + "\"temperatureStdDev\":" + record.TemperatureStdDev + ","
                + "\"humidityMin\":" + record.HumidityMin + ","
                + "\"humidityMax\":" + record.HumidityMax + ","
                + "\"humidityStdDev\":" + record.HumidityStdDev + ","
                + "\"pressureMin\":" + record.PressureMin + ","
                + "\"pressureMax\":" + record.PressureMax + ","
                + "\"pressureStdDev\":" + record.PressureStdDev + ","
                + "\"pm25Min\":" + record.Pm25Min + ","
                + "\"pm25Max\":" + record.Pm25Max + ","
                + "\"pm25StdDev\":" + record.Pm25StdDev + ","
                + "\"pm10Min\":" + record.Pm10Min + ","
                + "\"pm10Max\":" + record.Pm10Max + ","
                + "\"pm10StdDev\":" + record.Pm10StdDev + ","
                + "\"status\":\"" + record.Status.ToString() + "\""
                + "}";
        }

        /// <summary>
        /// Serializes one device event to compact JSON.
        /// </summary>
        private string SerializeEvent(NanoDeviceEvent deviceEvent)
        {
            return "{"
                + "\"stationGuid\":\"" + JsonEscape(GetStationGuid()) + "\","
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

        private string GetHardwareId()
        {
            return _identity == null || _identity.DeviceHardwareId == null ? string.Empty : _identity.DeviceHardwareId;
        }

        private string GetFirmwareVersion()
        {
            return _identity == null || _identity.FirmwareVersion == null ? string.Empty : _identity.FirmwareVersion;
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

    /// <summary>
    /// Moves queued records to the upload client after successful acknowledgement.
    /// </summary>
    public sealed class NanoMeasurementUploadService
    {
        private readonly NanoMeasurementQueue _queue;
        private readonly NanoUploadClient _client;

        /// <summary>
        /// Creates upload service for one measurement queue.
        /// </summary>
        public NanoMeasurementUploadService(NanoMeasurementQueue queue, NanoUploadClient client)
        {
            _queue = queue;
            _client = client;
        }

        /// <summary>
        /// Uploads at most one pending record.
        /// </summary>
        public bool TryUploadOne()
        {
            NanoMeasurementRecord record;
            if (!_queue.TryPeek(out record))
            {
                return false;
            }

            if (!_client.Upload(record))
            {
                return false;
            }

            _queue.TryDequeue(out record);
            return true;
        }

        /// <summary>
        /// Uploads pending records until the queue is empty, an upload fails, or the limit is reached.
        /// </summary>
        public int TryUploadPending(int maxItems)
        {
            int uploaded = 0;
            while (uploaded < maxItems && TryUploadOne())
            {
                uploaded++;
            }

            return uploaded;
        }
    }
}
