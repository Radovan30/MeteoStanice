namespace MeteoStanice.Data.Entities;

/// <summary>
/// One received sample batch (diagnostic log of the transport): how many samples were stored,
/// duplicated or rejected, upload delay and the queue state reported by the station.
/// Used to analyse delayed, duplicated and lost records.
/// </summary>
public sealed class SampleUploadBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StationId { get; set; }

    public Station Station { get; set; } = null!;

    /// <summary>
    /// UTC time the API received the batch.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public int ProtocolVersion { get; set; }

    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the station boot; a change between batches marks a restart.
    /// </summary>
    public Guid? BootId { get; set; }

    public int ReceivedCount { get; set; }

    public int StoredCount { get; set; }

    /// <summary>
    /// Samples already stored before (repeated upload) or repeated within the batch.
    /// </summary>
    public int DuplicateCount { get; set; }

    /// <summary>
    /// Samples not stored because of an implausible timestamp.
    /// </summary>
    public int RejectedCount { get; set; }

    public DateTimeOffset? FirstMeasuredAt { get; set; }

    public DateTimeOffset? LastMeasuredAt { get; set; }

    /// <summary>
    /// Largest delay between measurement and reception in the batch, in seconds.
    /// </summary>
    public double? MaxDelaySeconds { get; set; }

    public int? QueuePendingCount { get; set; }

    public int? DroppedRamCount { get; set; }

    public int? DroppedFlashCount { get; set; }

    public int? StationRejectedCount { get; set; }
}
