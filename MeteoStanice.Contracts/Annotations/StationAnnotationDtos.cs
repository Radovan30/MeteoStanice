namespace MeteoStanice.Contracts.Annotations;

/// <summary>
/// Allowed annotation categories. The split matters for the thesis: some categories describe
/// a real environmental event (the data are correct), others a sensor fault or an intervention.
/// </summary>
public static class AnnotationCategories
{
    /// <summary>Stations measure side by side at one place (inter-comparison, calibration).</summary>
    public const string Colocation = "Colocation";

    /// <summary>Placement of a station (outdoor/indoor, height, shelter) valid from StartsAt.</summary>
    public const string Placement = "Placement";

    /// <summary>Station moved to another place.</summary>
    public const string Relocation = "Relocation";

    /// <summary>Service intervention (opening the case, connector, cable).</summary>
    public const string Maintenance = "Maintenance";

    /// <summary>Cleaning of the SPS30 or inlet (manual or automatic fan cleaning).</summary>
    public const string Cleaning = "Cleaning";

    /// <summary>Calibration or reference comparison period.</summary>
    public const string Calibration = "Calibration";

    /// <summary>Deliberately induced anomaly (breath, smoke, heating, covering the sensor, unplugging).</summary>
    public const string InducedAnomaly = "InducedAnomaly";

    /// <summary>Confirmed sensor or hardware fault.</summary>
    public const string SensorFault = "SensorFault";

    /// <summary>Real environmental event (smog episode, fireworks, local burning, fog) – data are valid.</summary>
    public const string EnvironmentalEvent = "EnvironmentalEvent";

    /// <summary>Intervention on power supply (battery test, cable unplugged).</summary>
    public const string PowerIntervention = "PowerIntervention";

    /// <summary>Intervention on network or server (router restart, API maintenance).</summary>
    public const string NetworkIntervention = "NetworkIntervention";

    /// <summary>Firmware update of the station.</summary>
    public const string FirmwareUpdate = "FirmwareUpdate";

    /// <summary>Anything else.</summary>
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All =
    [
        Colocation, Placement, Relocation, Maintenance, Cleaning, Calibration, InducedAnomaly,
        SensorFault, EnvironmentalEvent, PowerIntervention, NetworkIntervention, FirmwareUpdate, Other
    ];

    /// <summary>
    /// Categories labelled as anomaly by default (can be overridden by IsAnomaly).
    /// </summary>
    public static bool IsAnomalyByDefault(string category)
        => category is InducedAnomaly or SensorFault;
}

/// <summary>
/// Annotation returned by the API.
/// </summary>
public sealed class StationAnnotationDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// Station GUID; null = all stations.
    /// </summary>
    public Guid? StationGuid { get; set; }

    public string StationCode { get; set; } = string.Empty;

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string AffectedChannels { get; set; } = string.Empty;

    public bool IsAnomaly { get; set; }

    public string Author { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Create or update request for an annotation.
/// </summary>
public sealed class StationAnnotationRequest
{
    /// <summary>
    /// Station GUID; null = all stations.
    /// </summary>
    public Guid? StationGuid { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public string Category { get; set; } = AnnotationCategories.Other;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Comma separated channel codes; empty = all channels.
    /// </summary>
    public string AffectedChannels { get; set; } = string.Empty;

    /// <summary>
    /// Null = derived from the category (InducedAnomaly and SensorFault are anomalies).
    /// </summary>
    public bool? IsAnomaly { get; set; }

    public string Author { get; set; } = string.Empty;
}
