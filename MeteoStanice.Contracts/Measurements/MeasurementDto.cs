namespace MeteoStanice.Contracts.Measurements;

/// <summary>
/// Measurement returned by the API to dashboards and clients.
/// </summary>
public sealed class MeasurementDto
{
    /// <summary>
    /// Database identifier of the measurement.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Stable GUID of the station that produced the measurement.
    /// </summary>
    public Guid StationGuid { get; set; }

    /// <summary>
    /// Station-local monotonically increasing measurement sequence.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// UTC timestamp when the measurement interval was closed.
    /// </summary>
    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>
    /// Average temperature in degrees Celsius.
    /// </summary>
    public double? TemperatureCelsius { get; set; }

    /// <summary>
    /// Average relative humidity in percent.
    /// </summary>
    public double? HumidityPercent { get; set; }

    /// <summary>
    /// Average atmospheric pressure in hPa.
    /// </summary>
    public double? PressureHpa { get; set; }

    /// <summary>
    /// Average PM2.5 concentration.
    /// </summary>
    public double? Pm25 { get; set; }

    /// <summary>
    /// Average PM10 concentration.
    /// </summary>
    public double? Pm10 { get; set; }

    /// <summary>
    /// Power source reported or inferred for the measurement.
    /// </summary>
    public string PowerSource { get; set; } = string.Empty;

    /// <summary>
    /// Quality flag count attached to this measurement.
    /// </summary>
    public int QualityFlagCount { get; set; }
}
