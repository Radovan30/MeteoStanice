namespace MeteoStanice.Contracts.Samples;

/// <summary>
/// Batch of raw samples uploaded by one station.
/// </summary>
public sealed class SensorSampleUploadRequest
{
    public int ProtocolVersion { get; set; }

    public Guid StationGuid { get; set; }

    public string StationCode { get; set; } = string.Empty;

    public string HardwareId { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>
    /// Free-text station location from the setup form (protocol 2+); empty keeps the stored value.
    /// </summary>
    public string LocationText { get; set; } = string.Empty;

    /// <summary>
    /// Random identifier of the current station boot (protocol 2+); a new value marks a restart.
    /// </summary>
    public Guid? BootId { get; set; }

    /// <summary>
    /// Samples waiting in the station queues when the batch was sent (protocol 2+).
    /// </summary>
    public int? QueuePendingCount { get; set; }

    /// <summary>
    /// Samples dropped from the full RAM queue since boot (protocol 2+).
    /// </summary>
    public int? DroppedRamCount { get; set; }

    /// <summary>
    /// Samples overwritten in the full flash queue, persistent across restarts (protocol 2+).
    /// </summary>
    public int? DroppedFlashCount { get; set; }

    /// <summary>
    /// Samples the station removed after a permanent rejection by the API, since boot (protocol 2+).
    /// </summary>
    public int? RejectedSampleCount { get; set; }

    public List<SensorSampleUploadItem> Samples { get; set; } = new();
}

/// <summary>
/// One raw sample in an upload batch.
/// </summary>
public sealed class SensorSampleUploadItem
{
    public long SequenceNumber { get; set; }

    public DateTimeOffset MeasuredAt { get; set; }

    public double? TemperatureCelsius { get; set; }

    public double? HumidityPercent { get; set; }

    public double? PressureHpa { get; set; }

    public double? Bmp280TemperatureCelsius { get; set; }

    public double? Pm1 { get; set; }

    public double? Pm25 { get; set; }

    public double? Pm4 { get; set; }

    public double? Pm10 { get; set; }

    public double? NumberPm05 { get; set; }

    public double? NumberPm1 { get; set; }

    public double? NumberPm25 { get; set; }

    public double? NumberPm4 { get; set; }

    public double? NumberPm10 { get; set; }

    public double? TypicalParticleSize { get; set; }

    public double? BatteryVoltage { get; set; }

    public bool BatteryLow { get; set; }

    public string PowerSource { get; set; } = string.Empty;

    public string Sht40Status { get; set; } = string.Empty;

    public string Bmp280Status { get; set; } = string.Empty;

    public string Sps30Status { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
