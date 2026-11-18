namespace MeteoStanice.Data.Entities;

/// <summary>
/// Manually recorded fact about a time period: intervention log, colocation, relocation, cleaning,
/// deliberately induced anomaly or a real environmental event. Serves as reference annotation
/// (ground truth) for distinguishing sensor errors from real events and for evaluating detectors.
/// </summary>
public sealed class StationAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Affected station; null = all stations (for example a regional smog episode).
    /// </summary>
    public Guid? StationId { get; set; }

    public Station? Station { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// End of the period; null = still valid (for example the current placement).
    /// </summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>
    /// Category code, see MeteoStanice.Contracts.Annotations.AnnotationCategories.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Comma separated channel codes (temperature, humidity, pressure, pm1, pm25, pm4, pm10, ...); empty = all.
    /// </summary>
    public string AffectedChannels { get; set; } = string.Empty;

    /// <summary>
    /// True when the period should be labelled as an anomaly in detector evaluation
    /// (induced anomaly or confirmed sensor fault); false for normal context such as colocation.
    /// </summary>
    public bool IsAnomaly { get; set; }

    public string Author { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
