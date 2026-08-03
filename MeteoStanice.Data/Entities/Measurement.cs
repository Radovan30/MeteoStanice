namespace MeteoStanice.Data.Entities;

/// <summary>
/// Aggregated measurement record received from a station.
/// </summary>
public sealed class Measurement
{
    /// <summary>
    /// Database identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Parent station identifier.
    /// </summary>
    public long StationId { get; set; }

    /// <summary>
    /// Parent station.
    /// </summary>
    public Station Station { get; set; } = null!;

    /// <summary>
    /// Station-local monotonically increasing sequence number.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// UTC timestamp when the interval was closed.
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
    /// Battery voltage captured with the measurement.
    /// </summary>
    public double? BatteryVoltage { get; set; }

    /// <summary>
    /// Power source, for example BAT or SRC.
    /// </summary>
    public string PowerSource { get; set; } = string.Empty;

    /// <summary>
    /// Firmware measurement status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when API accepted the record.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Quality flags attached to this measurement.
    /// </summary>
    public ICollection<MeasurementQualityFlag> QualityFlags { get; set; } = new List<MeasurementQualityFlag>();
}
