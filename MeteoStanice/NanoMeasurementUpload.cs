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
        // True when simulated server accepts records.
        public bool ServerAvailable { get; set; }
        // True when HTTP upload is enabled after API health check.
        private bool _httpEnabled;
        // Configured API base URL.
        private string _apiBaseUrl;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Creates an upload client with simulated server available.
        /// </summary>
        public NanoUploadClient()
        {
            ServerAvailable = true;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Enables or disables real HTTP upload after server health check.
        /// </summary>
        public void ConfigureHttp(string apiBaseUrl, bool httpEnabled)
        {
            _apiBaseUrl = apiBaseUrl;
            _httpEnabled = httpEnabled && apiBaseUrl != null && apiBaseUrl.Length > 0;
            Debug.WriteLine("Upload HTTP=" + _httpEnabled + ", API=" + (_apiBaseUrl == null || _apiBaseUrl.Length == 0 ? "<nenastaveno>" : _apiBaseUrl));
        }

        /// <summary>
        /// Uploads one measurement record to the simulated server.
        /// </summary>
        public bool Upload(NanoMeasurementRecord record)
        {
            if (!ServerAvailable || record == null)
            {
                return false;
            }

            if (_httpEnabled)
            {
                return PostJson(BuildUrl("measurements"), SerializeMeasurement(record), "record #" + record.SequenceNumber);
            }

            Debug.WriteLine("Uploaded record #" + record.SequenceNumber + " from " + record.StationId);
            return true;
        }

        /// <summary>
        /// Uploads one device event to the simulated server.
        /// </summary>
        public bool UploadEvent(NanoDeviceEvent deviceEvent)
        {
            if (!ServerAvailable || deviceEvent == null)
            {
                return false;
            }

            if (_httpEnabled)
            {
                return PostJson(BuildUrl("events"), SerializeEvent(deviceEvent), "event #" + deviceEvent.SequenceNumber);
            }

            Debug.WriteLine("Uploaded event #" + deviceEvent.SequenceNumber + " " + deviceEvent.Code.ToString());
            return true;
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

            try
            {
                content = new StringContent(json, Encoding.UTF8, "application/json");
                response = _httpClient.Post(url, content);
                Debug.WriteLine("HTTP upload " + label + " status=" + (int)response.StatusCode);
                return response.IsSuccessStatusCode;
            }
            catch (System.Exception exception)
            {
                Debug.WriteLine("HTTP upload " + label + " failed: " + exception.Message);
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
            }
        }

        /// <summary>
        /// Serializes one measurement record to compact JSON.
        /// </summary>
        private string SerializeMeasurement(NanoMeasurementRecord record)
        {
            return "{"
                + "\"stationId\":\"" + JsonEscape(record.StationId) + "\","
                + "\"sequenceNumber\":" + record.SequenceNumber + ","
                + "\"sampledAt\":\"" + JsonEscape(record.SampledAt.ToString()) + "\","
                + "\"temperatureCelsius\":" + record.TemperatureCelsius + ","
                + "\"relativeHumidityPercent\":" + record.RelativeHumidityPercent + ","
                + "\"pressureHpa\":" + record.PressureHpa + ","
                + "\"pm25\":" + record.Pm25 + ","
                + "\"pm10\":" + record.Pm10 + ","
                + "\"validValueCount\":" + record.ValidValueCount + ","
                + "\"failedReadCount\":" + record.FailedReadCount + ","
                + "\"status\":\"" + record.Status.ToString() + "\""
                + "}";
        }

        /// <summary>
        /// Serializes one device event to compact JSON.
        /// </summary>
        private string SerializeEvent(NanoDeviceEvent deviceEvent)
        {
            return "{"
                + "\"stationId\":\"" + JsonEscape(deviceEvent.StationId) + "\","
                + "\"sequenceNumber\":" + deviceEvent.SequenceNumber + ","
                + "\"occurredAt\":\"" + JsonEscape(deviceEvent.OccurredAt.ToString()) + "\","
                + "\"severity\":\"" + deviceEvent.Severity.ToString() + "\","
                + "\"category\":\"" + deviceEvent.Category.ToString() + "\","
                + "\"code\":\"" + deviceEvent.Code.ToString() + "\","
                + "\"component\":\"" + JsonEscape(deviceEvent.Component) + "\","
                + "\"message\":\"" + JsonEscape(deviceEvent.Message) + "\""
                + "}";
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
