namespace MeteoStanice.Contracts.Stations;

public sealed class StationDto
{
    public Guid StationGuid { get; set; }

    public string StationCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string LocationText { get; set; } = string.Empty;

    public string HardwareId { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    public int ProtocolVersion { get; set; }

    public double Sps30CorrectionPercent { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public bool IsActive { get; set; }
}

public sealed class UpdateSps30CorrectionRequest
{
    /// <summary>
    /// Signed correction in percent. For example 5 increases the reported value by 5 percent.
    /// </summary>
    public double CorrectionPercent { get; set; }
}
