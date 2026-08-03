namespace MeteoStanice.Data.Entities;

/// <summary>
/// Current remotely managed station settings.
/// </summary>
public sealed class StationSetting
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
    /// Measurement interval in minutes.
    /// </summary>
    public int MeasurementIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Sensor sample period in seconds.
    /// </summary>
    public int SamplePeriodSeconds { get; set; } = 1;

    /// <summary>
    /// Manually configured station location.
    /// </summary>
    public string LocationText { get; set; } = string.Empty;

    /// <summary>
    /// Monotonic settings version used for station synchronization.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// UTC timestamp when settings were last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
