namespace MeteoStanice.Data.Entities;

/// <summary>
/// Event emitted by station firmware or backend services.
/// </summary>
public sealed class DeviceEvent
{
    /// <summary>
    /// Database identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Parent station identifier.
    /// </summary>
    public Guid StationId { get; set; }

    /// <summary>
    /// Parent station.
    /// </summary>
    public Station Station { get; set; } = null!;

    /// <summary>
    /// Station-local event sequence number.
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

    /// <summary>
    /// UTC timestamp when API accepted the event.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the event was resolved.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}
