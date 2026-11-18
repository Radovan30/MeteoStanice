using MeteoStanice.Contracts.Statuses;

namespace MeteoStanice.Monitor.Models;

/// <summary>
/// Zrcadlo vyctu z firmwaru (MeteoStanice/Enums.cs). nanoFramework muze pri Enum.ToString()
/// poslat ciselnou hodnotu, proto se cisla prevadi podle poradi polozek.
/// PORADI MUSI ODPOVIDAT FIRMWARU.
/// </summary>
public static class FirmwareEnums
{
    /// <summary>
    /// MeasurementStatus (docs/ARCHITEKTURA_A_POPIS_SYSTEMU.md, kap. 11.4).
    /// </summary>
    public static readonly string[] MeasurementStatusNames = FirmwareStatusCatalog.MeasurementStatusNames;

    /// <summary>
    /// DeviceEventSeverity.
    /// </summary>
    public static readonly string[] SeverityNames = FirmwareStatusCatalog.EventSeverityNames;

    /// <summary>
    /// DeviceEventCode.
    /// </summary>
    public static readonly string[] EventCodeNames = FirmwareStatusCatalog.EventCodeNames;

    /// <summary>
    /// Poruchova udalost -> skupina (komponenta), ktera je v poruse.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FailureGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Sht40NotDetected"] = "SHT40",
        ["Sht40ReadFailed"] = "SHT40",
        ["Sht40CrcFailed"] = "SHT40",
        ["Bmp280NotDetected"] = "BMP280",
        ["Bmp280ReadFailed"] = "BMP280",
        ["Sps30NotDetected"] = "SPS30",
        ["Sps30ReadFailed"] = "SPS30",
        ["Sps30FanError"] = "SPS30",
        ["Sps30LaserError"] = "SPS30",
        ["Sps30DisabledOnBattery"] = "SPS30",
        ["MainsPowerLost"] = "Power",
        ["BackupPowerActivated"] = "Power",
        ["BatteryLow"] = "Battery",
        ["BrownoutDetected"] = "Battery",
        ["WifiConnectionFailed"] = "WiFi",
        ["ServerUnavailable"] = "Server",
        ["AuthenticationFailed"] = "Server",
        ["ClockSynchronizationFailed"] = "Clock",
        ["LocalQueueNearFull"] = "Queue",
        ["LocalQueueFull"] = "Queue",
        ["LocalRecordCorrupted"] = "Queue",
        ["ConfigurationInvalid"] = "Config"
    };

    /// <summary>
    /// Udalost zotaveni -> skupiny, ktere ukoncuje.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RecoveryGroups = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Sht40Recovered"] = ["SHT40"],
        ["Bmp280Recovered"] = ["BMP280"],
        ["Sps30Restored"] = ["SPS30"],
        ["MainsPowerRestored"] = ["Power", "Battery"],
        ["WifiConnected"] = ["WiFi"],
        ["ServerConnectionRestored"] = ["Server"],
        ["ClockSynchronized"] = ["Clock"],
        ["ConfigurationApplied"] = ["Config"]
    };

    /// <summary>
    /// Skupiny bez parove udalosti zotaveni – zobrazuji se jen omezenou dobu.
    /// </summary>
    public static readonly IReadOnlySet<string> TimeLimitedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Queue" };

    /// <summary>
    /// Ceske popisky skupin.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> GroupLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SHT40"] = "SHT40 (teplota/vlhkost)",
        ["BMP280"] = "BMP280 (tlak)",
        ["SPS30"] = "SPS30 (prachové částice)",
        ["Power"] = "Napájení",
        ["Battery"] = "Baterie",
        ["WiFi"] = "Wi-Fi",
        ["Server"] = "Spojení se serverem",
        ["Clock"] = "Synchronizace času",
        ["Queue"] = "Lokální fronta",
        ["Config"] = "Konfigurace"
    };

    /// <summary>
    /// Kratke texty poruchovych udalosti pro zobrazeni u ukazatele.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> EventShortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Sht40NotDetected"] = "SHT40 nenalezen",
        ["Sht40ReadFailed"] = "chyba čtení SHT40",
        ["Sht40CrcFailed"] = "chyba CRC SHT40",
        ["Bmp280NotDetected"] = "BMP280 nenalezen",
        ["Bmp280ReadFailed"] = "chyba čtení BMP280",
        ["Sps30NotDetected"] = "SPS30 nenalezen",
        ["Sps30ReadFailed"] = "chyba čtení SPS30",
        ["Sps30FanError"] = "chyba ventilátoru SPS30",
        ["Sps30LaserError"] = "chyba laseru SPS30",
        ["Sps30DisabledOnBattery"] = "SPS30 vypnut (baterie)",
        ["MainsPowerLost"] = "výpadek sítě",
        ["BackupPowerActivated"] = "běh na baterii",
        ["BatteryLow"] = "nízké napětí",
        ["BrownoutDetected"] = "podpětí"
    };

    /// <summary>
    /// Kratke texty kontrol kvality (MeteoStanice.Quality).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> QualityCheckShortTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MissingValue"] = "chybí hodnota",
        ["OutOfRange"] = "mimo rozsah",
        ["SuddenJump"] = "skoková změna",
        ["Flatline"] = "podezřele konstantní",
        ["RepeatedConstant"] = "konstantní 2 intervaly",
        ["LowCompleteness"] = "málo platných vzorků",
        ["ParticleMassOrder"] = "nesoulad PM frakcí"
    };

    /// <summary>
    /// Prevede ciselnou nebo textovou podobu vyctu na nazev polozky.
    /// </summary>
    public static string Normalize(string? raw, string[] names)
        => FirmwareStatusCatalog.Normalize(raw, names);

    /// <summary>
    /// Vrati poradi zavaznosti (0 = Information ... 3 = Critical).
    /// </summary>
    public static int SeverityRank(string? raw)
    {
        string name = Normalize(raw, SeverityNames);
        int index = Array.FindIndex(SeverityNames, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 1 : index;
    }
}
