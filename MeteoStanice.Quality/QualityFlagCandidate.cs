namespace MeteoStanice.Quality;

/// <summary>
/// Quality flag produced before it is mapped to database lookup rows.
/// </summary>
public sealed class QualityFlagCandidate
{
    /// <summary>
    /// Sensor channel code, for example temperature or pm25.
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// Quality check code.
    /// </summary>
    public string CheckCode { get; set; } = string.Empty;

    /// <summary>
    /// Flag severity.
    /// </summary>
    public string Severity { get; set; } = "Warning";

    /// <summary>
    /// Human readable explanation.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
