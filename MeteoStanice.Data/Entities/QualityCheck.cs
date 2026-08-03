namespace MeteoStanice.Data.Entities;

/// <summary>
/// Definition of one data quality check.
/// </summary>
public sealed class QualityCheck
{
    /// <summary>
    /// Database identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Stable check code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human readable check name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Explanation of the check.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Default severity for flags produced by the check.
    /// </summary>
    public string DefaultSeverity { get; set; } = "Warning";

    /// <summary>
    /// True when check is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
