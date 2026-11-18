namespace MeteoStanice.Contracts.Samples;

/// <summary>
/// Raw sensor sample returned by the backend.
/// </summary>
public sealed class SensorSampleDto
{
    public Guid Id { get; set; }

    public Guid StationGuid { get; set; }

    public string StationCode { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public long SequenceNumber { get; set; }

    public DateTimeOffset MeasuredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

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

/// <summary>
/// One page of raw samples and paging metadata.
/// </summary>
public sealed class SensorSamplePageDto
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public long TotalCount { get; set; }

    public IReadOnlyList<SensorSampleDto> Items { get; set; } = Array.Empty<SensorSampleDto>();
}

/// <summary>
/// Result of an idempotent batch upload.
/// </summary>
public sealed class SensorSampleUploadResultDto
{
    public Guid StationGuid { get; set; }

    public string StationCode { get; set; } = string.Empty;

    public int ReceivedCount { get; set; }

    public int StoredCount { get; set; }

    public int DuplicateCount { get; set; }

    /// <summary>
    /// Samples skipped because their timestamp was implausible (station clock not synchronized).
    /// They are acknowledged so the station removes them from its queue.
    /// </summary>
    public int RejectedCount { get; set; }
}
