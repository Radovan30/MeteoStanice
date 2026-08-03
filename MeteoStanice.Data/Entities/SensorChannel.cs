namespace MeteoStanice.Data.Entities;

/// <summary>
/// Sensor channel that can be validated by quality checks.
/// </summary>
public sealed class SensorChannel
{
    /// <summary>
    /// Database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Stable channel code, for example temperature or pm25.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human readable channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Physical unit shown in dashboards.
    /// </summary>
    public string Unit { get; set; } = string.Empty;
}
