namespace MeteoStanice.Contracts.Events;

/// <summary>
/// Device event payload sent by station firmware to the backend API.
/// </summary>
public sealed class DeviceEventUploadRequest
{
    /// <summary>
    /// Stable station GUID generated and stored by the firmware.
    /// </summary>
    public Guid StationGuid { get; set; }

    /// <summary>
    /// Legacy station identifier used by the current nanoFramework firmware.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Human readable station code used by operators.
    /// </summary>
    public string StationCode { get; set; } = string.Empty;

    /// <summary>
    /// Station-local monotonically increasing event sequence.
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
    /// Event subsystem category.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Stable machine-readable event code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Component that produced the event.
    /// </summary>
    public string Component { get; set; } = string.Empty;

    /// <summary>
    /// Short diagnostic message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
