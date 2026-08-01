using System.Diagnostics;
using System.Net.Http;

namespace MeteoStanice
{
    /// <summary>
    /// Describes the current API server readiness.
    /// </summary>
    public enum NanoServerConnectionState
    {
        // Server URL is not configured.
        NotConfigured,
        // Wi-Fi is not connected yet.
        WaitingForWifi,
        // Server check can be attempted.
        ReadyForCheck,
        // Server responded successfully.
        Available,
        // Server check failed.
        Unavailable
    }

    /// <summary>
    /// Stores a small API server status snapshot.
    /// </summary>
    public sealed class NanoServerStatus
    {
        // Current server connection state.
        public NanoServerConnectionState State { get; set; }
        // Configured API base URL.
        public string ApiBaseUrl { get; set; }
        // Short diagnostic message.
        public string Message { get; set; }
        // HTTP status code returned by the API server, or 0 when no request was made.
        public int HttpStatusCode { get; set; }
    }

    /// <summary>
    /// Evaluates whether API server communication can be attempted.
    /// </summary>
    public sealed class NanoServerStatusService
    {
        private readonly NanoDeviceEventService _eventService;

        /// <summary>
        /// Creates server status service backed by device events.
        /// </summary>
        public NanoServerStatusService(NanoDeviceEventService eventService)
        {
            _eventService = eventService;
        }

        /// <summary>
        /// Checks whether firmware has enough configuration and network state to contact the API server.
        /// </summary>
        public NanoServerStatus Check(StationConfiguration configuration, bool wifiConnected, string stationId)
        {
            NanoServerStatus status = new NanoServerStatus();
            status.ApiBaseUrl = configuration.ApiBaseUrl;

            if (configuration.ApiBaseUrl == null || configuration.ApiBaseUrl.Length == 0)
            {
                status.State = NanoServerConnectionState.NotConfigured;
                status.Message = "API URL neni nastavena, server test preskocen.";
                Debug.WriteLine("Server: " + status.Message);
                return status;
            }

            if (!wifiConnected)
            {
                status.State = NanoServerConnectionState.WaitingForWifi;
                status.Message = "Wi-Fi neni pripojena, server test preskocen.";
                _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Network, DeviceEventCode.ServerUnavailable, "Server", status.Message);
                Debug.WriteLine("Server: " + status.Message);
                return status;
            }

            status.State = NanoServerConnectionState.ReadyForCheck;
            status.Message = "API URL je nastavena, spoustim HTTP health check.";
            Debug.WriteLine("Server: " + status.Message + " URL=" + status.ApiBaseUrl);
            RunHealthCheck(status, stationId);
            return status;
        }

        /// <summary>
        /// Runs a small HTTP GET check against configured API URL.
        /// </summary>
        private void RunHealthCheck(NanoServerStatus status, string stationId)
        {
            HttpResponseMessage response = null;

            try
            {
                HttpClient client = new HttpClient();
                response = client.Get(status.ApiBaseUrl);
                status.HttpStatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    status.State = NanoServerConnectionState.Available;
                    status.Message = "API server odpovedel uspesne.";
                    _eventService.ClearActive();
                    _eventService.Report(stationId, DeviceEventSeverity.Information, DeviceEventCategory.Network, DeviceEventCode.ServerConnectionRestored, "Server", status.Message);
                    Debug.WriteLine("Server: " + status.Message + " HTTP=" + status.HttpStatusCode);
                    return;
                }

                status.State = NanoServerConnectionState.Unavailable;
                status.Message = "API server vratil chybu.";
                _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Network, DeviceEventCode.ServerUnavailable, "Server", status.Message);
                Debug.WriteLine("Server: " + status.Message + " HTTP=" + status.HttpStatusCode);
            }
            catch (System.Exception exception)
            {
                status.State = NanoServerConnectionState.Unavailable;
                status.HttpStatusCode = 0;
                status.Message = "API server neni dostupny.";
                _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Network, DeviceEventCode.ServerUnavailable, "Server", status.Message);
                Debug.WriteLine("Server: " + status.Message + " " + exception.Message);
            }
            finally
            {
                if (response != null)
                {
                    response.Dispose();
                }
            }
        }
    }
}
