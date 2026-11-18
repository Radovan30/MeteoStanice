namespace MeteoStanice.Data.Entities;

/// <summary>
/// One raw sensor sample reported by a station before interval aggregation.
/// </summary>
public sealed class SensorSample
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StationId { get; set; }

    public Station Station { get; set; } = null!;

    /// <summary>
    /// Station-generated sample identifier, normally UTC yyyyMMddHHmmss.
    /// </summary>
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

    /// <summary>
    /// SPS30 number concentrations in #/cm3 (particles larger than the given size); null for protocol 1 firmware.
    /// </summary>
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

    /// <summary>
    /// UTC timestamp when the backend accepted the sample.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// How many times the station delivered this sample (1 = once; more = repeated upload, e.g. lost HTTP response).
    /// </summary>
    public int ReceiveCount { get; set; } = 1;

    /// <summary>
    /// UTC time of the last repeated delivery; null when the sample arrived only once.
    /// </summary>
    public DateTimeOffset? LastReceivedAt { get; set; }
}
