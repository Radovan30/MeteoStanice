using Microsoft.AspNetCore.SignalR;

namespace MeteoStanice.Api.Hubs;

/// <summary>
/// SignalR hub for live dashboard updates.
/// </summary>
public sealed class MeteoHub : Hub
{
    /// <summary>
    /// Client method name raised when a new measurement is accepted.
    /// </summary>
    public const string MeasurementReceivedMethod = "measurementReceived";
}
