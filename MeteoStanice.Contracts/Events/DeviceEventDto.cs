namespace MeteoStanice.Contracts.Events;

/// <summary>
/// Device event returned by the API.
/// </summary>
public sealed class DeviceEventDto
{
    /// <summary>
    /// Database identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Stable station GUID.
    /// </summary>
    public Guid StationGuid { get; set; }

    /// <summary>
    /// Human-readable station code assigned by the backend.
    /// </summary>
    public string StationCode { get; set; } = string.Empty;

    /// <summary>
    /// Display name assigned to the station.
    /// </summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>
    /// Station-local event sequence.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Event severity.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Event category.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Event code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Component that produced the event.
    /// </summary>
    public string Component { get; set; } = string.Empty;

    /// <summary>
    /// Event message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
