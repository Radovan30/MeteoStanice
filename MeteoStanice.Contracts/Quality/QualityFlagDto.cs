namespace MeteoStanice.Contracts.Quality;

/// <summary>
/// Data quality flag produced by backend validation.
/// </summary>
public sealed class QualityFlagDto
{
    /// <summary>
    /// Sensor channel affected by the quality flag.
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// Quality check that produced the flag.
    /// </summary>
    public string CheckCode { get; set; } = string.Empty;

    /// <summary>
    /// Severity assigned to the flag.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Human readable explanation for dashboard users.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
