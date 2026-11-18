namespace MeteoStanice.Contracts.Statuses;

public enum SensorErrorCode
{
    None = 0,
    NotDetected = 1,
    Timeout = 2,
    CrcFailed = 3,
    InvalidResponse = 4,
    CommunicationFailed = 5,
    FanError = 6,
    LaserError = 7,
    DisabledOnBattery = 8
}

public enum MeasurementStatusCode
{
    Ok = 0,
    InsufficientSamples = 1,
    Sht40ReadFailed = 2,
    Bmp280ReadFailed = 3,
    Sps30ReadFailed = 4,
    BackupPowerActive = 5,
    Sps30DisabledOnBattery = 6
}

public enum DeviceEventCategoryCode
{
    Sensor = 0,
    Communication = 1,
    Network = 2,
    Time = 3,
    Power = 4,
    Storage = 5,
    Configuration = 6,
    System = 7
}

public enum DeviceEventSeverityCode
{
    Information = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public enum DeviceEventCode
{
    DeviceStarted = 0,
    DeviceRestarted = 1,
    MainsPowerLost = 2,
    MainsPowerRestored = 3,
    BackupPowerActivated = 4,
    BatteryLow = 5,
    BrownoutDetected = 6,
    Sht40NotDetected = 7,
    Sht40ReadFailed = 8,
    Sht40CrcFailed = 9,
    Sht40Recovered = 10,
    Bmp280NotDetected = 11,
    Bmp280ReadFailed = 12,
    Bmp280Recovered = 13,
    Sps30NotDetected = 14,
    Sps30ReadFailed = 15,
    Sps30FanError = 16,
    Sps30LaserError = 17,
    Sps30DisabledOnBattery = 18,
    Sps30Restored = 19,
    WifiConnectionFailed = 20,
    WifiConnected = 21,
    ServerUnavailable = 22,
    ServerConnectionRestored = 23,
    AuthenticationFailed = 24,
    ClockSynchronizationFailed = 25,
    ClockSynchronized = 26,
    LocalQueueNearFull = 27,
    LocalQueueFull = 28,
    LocalRecordCorrupted = 29,
    ConfigurationInvalid = 30,
    ConfigurationApplied = 31,
    SetupModeEntered = 32,
    SetupModeExited = 33
}

public sealed class StatusDefinitionDto
{
    public int Value { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class FirmwareStatusMetadataDto
{
    public int ProtocolVersion { get; init; }
    public IReadOnlyList<StatusDefinitionDto> MeasurementStatuses { get; init; } = [];
    public IReadOnlyList<StatusDefinitionDto> SensorErrors { get; init; } = [];
    public IReadOnlyList<StatusDefinitionDto> EventSeverities { get; init; } = [];
    public IReadOnlyList<StatusDefinitionDto> EventCategories { get; init; } = [];
    public IReadOnlyList<StatusDefinitionDto> EventCodes { get; init; } = [];
    public IReadOnlyList<StatusDefinitionDto> RawSampleStatuses { get; init; } = [];
    public IReadOnlyList<StatusDefinitionDto> PowerSources { get; init; } = [];
}

/// <summary>
/// Shared numeric-to-name mapping for firmware, API and user applications.
/// Numeric values are part of the persisted protocol and must not be reordered.
/// </summary>
public static class FirmwareStatusCatalog
{
    public static readonly string[] MeasurementStatusNames = Enum.GetNames<MeasurementStatusCode>();
    public static readonly string[] SensorErrorNames = Enum.GetNames<SensorErrorCode>();
    public static readonly string[] EventSeverityNames = Enum.GetNames<DeviceEventSeverityCode>();
    public static readonly string[] EventCategoryNames = Enum.GetNames<DeviceEventCategoryCode>();
    public static readonly string[] EventCodeNames = Enum.GetNames<DeviceEventCode>();

    public static string NormalizeMeasurementStatus(string? value) => Normalize(value, MeasurementStatusNames);
    public static string NormalizeSensorError(string? value) => Normalize(value, SensorErrorNames);
    public static string NormalizeEventSeverity(string? value) => Normalize(value, EventSeverityNames);
    public static string NormalizeEventCategory(string? value) => Normalize(value, EventCategoryNames);
    public static string NormalizeEventCode(string? value) => Normalize(value, EventCodeNames);

    public static string NormalizeRawSampleStatus(string? value)
        => NormalizeCode(value, ["OK", "DEGRADED", "BATTERY_LOW"]);

    public static string NormalizePowerSource(string? value)
        => NormalizeCode(value, ["SRC", "BAT"]);

    public static FirmwareStatusMetadataDto CreateMetadata() => new()
    {
        ProtocolVersion = Protocol.FirmwareProtocol.CurrentVersion,
        MeasurementStatuses = Definitions(MeasurementStatusNames,
        [
            "V poradku", "Nedostatek vzorku", "Chyba SHT40", "Chyba BMP280",
            "Chyba SPS30", "Aktivni zalozni napajeni", "SPS30 vypnut na baterii"
        ]),
        SensorErrors = Definitions(SensorErrorNames,
        [
            "Bez chyby", "Senzor nenalezen", "Vyprsel cas", "Chyba CRC",
            "Neplatna odpoved", "Chyba komunikace", "Chyba ventilatoru",
            "Chyba laseru", "Vypnuto pri provozu na baterii"
        ]),
        EventSeverities = Definitions(EventSeverityNames,
            ["Informace", "Varovani", "Chyba", "Kriticka chyba"]),
        EventCategories = Definitions(EventCategoryNames,
        [
            "Senzor", "Komunikace", "Sit", "Cas", "Napajeni", "Uloziste",
            "Konfigurace", "System"
        ]),
        EventCodes = Definitions(EventCodeNames, EventCodeNames),
        RawSampleStatuses = Definitions(["OK", "DEGRADED", "BATTERY_LOW"],
            ["V poradku", "Omezena kvalita", "Nizke napeti baterie"]),
        PowerSources = Definitions(["SRC", "BAT"], ["Externi zdroj", "Baterie"])
    };

    public static string Normalize(string? raw, IReadOnlyList<string> names)
    {
        string value = raw?.Trim() ?? string.Empty;
        if (int.TryParse(value, out int index) && index >= 0 && index < names.Count)
        {
            return names[index];
        }

        return CanonicalName(value, names);
    }

    private static string NormalizeCode(string? raw, IReadOnlyList<string> names)
        => Normalize(raw, names);

    private static string CanonicalName(string value, IReadOnlyList<string> names)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(value, names[index], StringComparison.OrdinalIgnoreCase))
            {
                return names[index];
            }
        }

        return value;
    }

    private static IReadOnlyList<StatusDefinitionDto> Definitions(
        IReadOnlyList<string> names,
        IReadOnlyList<string> labels)
    {
        List<StatusDefinitionDto> result = new(names.Count);
        for (int index = 0; index < names.Count; index++)
        {
            result.Add(new StatusDefinitionDto
            {
                Value = index,
                Code = names[index],
                Label = labels[index]
            });
        }

        return result;
    }
}
