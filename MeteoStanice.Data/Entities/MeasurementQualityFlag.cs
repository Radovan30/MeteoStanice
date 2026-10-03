namespace MeteoStanice.Data.Entities;

/// <summary>
/// Concrete data quality flag attached to one measurement.
/// </summary>
public sealed class MeasurementQualityFlag
{
    /// <summary>
    /// Database identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Measurement identifier.
    /// </summary>
    public Guid MeasurementId { get; set; }

    /// <summary>
    /// Measurement that owns the flag.
    /// </summary>
    public Measurement Measurement { get; set; } = null!;

    /// <summary>
    /// Sensor channel identifier.
    /// </summary>
    public int SensorChannelId { get; set; }

    /// <summary>
    /// Sensor channel affected by the flag.
    /// </summary>
    public SensorChannel SensorChannel { get; set; } = null!;

    /// <summary>
    /// Quality check identifier.
    /// </summary>
    public int QualityCheckId { get; set; }

    /// <summary>
    /// Quality check that produced the flag.
    /// </summary>
    public QualityCheck QualityCheck { get; set; } = null!;

    /// <summary>
    /// Severity for this concrete flag.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Human readable explanation.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the flag was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
