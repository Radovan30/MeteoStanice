using System;
using System.Diagnostics;
using System.Net.Http;
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
        private readonly HttpClient _httpClient;
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
            _httpClient = new HttpClient();
            try
            {
                _httpClient.Timeout = new TimeSpan(0, 0, 4);
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("HTTP timeout nelze nastavit: " + exception.Message);
            }
        }

        /// <summary>
        /// Stores identity fields included in upload payloads.
        /// </summary>
        public void ConfigureIdentity(StationIdentity identity)
        {
            _identity = identity;
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
            HttpResponseMessage response = null;
            StringContent content = null;
            HttpRequestMessage request = null;

            try
            {
                content = new StringContent(json, Encoding.UTF8, "application/json");
                request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                if (_apiKey != null && _apiKey.Length > 0)
                {
                    request.Headers.Add("X-Api-Key", _apiKey);
                }

                response = _httpClient.Send(request);
                Debug.WriteLine("HTTP upload " + label + " status=" + (int)response.StatusCode);
                MarkUploadResult(response.IsSuccessStatusCode, (int)response.StatusCode);
                if (response.IsSuccessStatusCode)
                {
                    _nextHttpAttemptTicks = 0;
                }
                else
                {
                    BackoffHttpAttempts();
                }

                return response.IsSuccessStatusCode;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("HTTP upload " + label + " failed: " + exception.Message);
                MarkUploadResult(false, 0);
                BackoffHttpAttempts();
                return false;
            }
            finally
            {
                if (response != null)
                {
                    response.Dispose();
                }

                if (content != null)
                {
                    content.Dispose();
                }

                if (request != null)
                {
                    request.Dispose();
                }
            }
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

        /// <summary>
        /// Serializes one measurement record to compact JSON.
        /// </summary>
        private string SerializeMeasurement(NanoMeasurementRecord record)
        {
            return "{"
                + "\"stationGuid\":\"" + JsonEscape(GetStationGuid()) + "\","
                + "\"stationId\":\"" + JsonEscape(record.StationId) + "\","
                + "\"stationCode\":\"" + JsonEscape(record.StationId) + "\","
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
