using MeteoStanice.Data.Entities;

namespace MeteoStanice.Monitor.Models;

/// <summary>
/// Data jedne stanice nactena z databaze pri jednom obnoveni.
/// </summary>
public sealed class StationSnapshot
{
    /// <summary>
    /// Stanice.
    /// </summary>
    public required Station Station { get; init; }

    /// <summary>
    /// Posledni prijate mereni (nebo null).
    /// </summary>
    public Measurement? LatestMeasurement { get; init; }

    /// <summary>
    /// Kvalitativni priznaky posledniho mereni.
    /// </summary>
    public IReadOnlyList<MeasurementQualityFlag> LatestFlags { get; init; } = [];

    /// <summary>
    /// Udalosti stanice za sledovane obdobi, chronologicky.
    /// </summary>
    public IReadOnlyList<DeviceEvent> RecentEvents { get; init; } = [];

    /// <summary>
    /// Interval mereni dle StationSettings (0 = nenastaveno).
    /// </summary>
    public int MeasurementIntervalMinutes { get; init; }

    /// <summary>
    /// Pocet zaznamu s casem mereni v budoucnosti (ignoruji se – chybny cas stanice).
    /// </summary>
    public int FutureMeasurementCount { get; init; }
}

/// <summary>
/// Celkovy stav stanice. Poradi odpovida zavaznosti.
/// </summary>
public enum StationHealth
{
    /// <summary>Bez problemu.</summary>
    Ok = 0,

    /// <summary>Degradovany stav.</summary>
    Warning = 1,

    /// <summary>Chyba senzoru nebo komponenty.</summary>
    Error = 2,

    /// <summary>Stanice neposila data.</summary>
    Offline = 3
}

/// <summary>
/// Jeden zjisteny problem stanice.
/// </summary>
/// <param name="Key">Stabilni klic (pro alarm a jeho potvrzeni).</param>
/// <param name="Severity">Zavaznost.</param>
/// <param name="Message">Popis pro uzivatele.</param>
public sealed record StationIssue(string Key, StationHealth Severity, string Message);

/// <summary>
/// Vysledek vyhodnoceni stavu stanice.
/// </summary>
public sealed class StationEvaluation
{
    /// <summary>
    /// Nejhorsi zjisteny stav.
    /// </summary>
    public StationHealth Health { get; init; }

    /// <summary>
    /// True, pokud zobrazene hodnoty nejsou aktualni.
    /// </summary>
    public bool IsStale { get; init; }

    /// <summary>
    /// Seznam problemu serazeny od nejzavaznejsiho.
    /// </summary>
    public IReadOnlyList<StationIssue> Issues { get; init; } = [];

    /// <summary>
    /// Problemy prirazene konkretnim senzorum/velicinam (zobrazuji se primo u ukazatelu).
    /// </summary>
    public IReadOnlyList<SensorProblem> SensorProblems { get; init; } = [];
}

/// <summary>
/// Problem prirazeny senzoru (SHT40, BMP280, SPS30, Battery) nebo konkretni velicine (temperature, pm25...).
/// </summary>
/// <param name="Target">Nazev senzoru nebo kod veliciny.</param>
/// <param name="Severity">Zavaznost.</param>
/// <param name="Text">Kratky text pod ukazatelem.</param>
public sealed record SensorProblem(string Target, StationHealth Severity, string Text);

/// <summary>
/// Prirazeni velicin k senzorum.
/// </summary>
public static class SensorMap
{
    /// <summary>
    /// Vrati senzor, ktery velicinu meri.
    /// </summary>
    public static string SensorFor(string code)
    {
        string normalized = code.ToLowerInvariant();
        return normalized switch
        {
            "temperature" or "humidity" or "sht40" => "SHT40",
            "pressure" or "pressuresealevel" or "bmp280" => "BMP280",
            "batteryvoltage" => "Battery",
            _ when normalized.StartsWith("pm", StringComparison.Ordinal)
                || normalized.StartsWith("numberpm", StringComparison.Ordinal)
                || normalized is "typicalparticlesize" or "sps30" => "SPS30",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Kanal v DB, ze ktereho je velicina odvozena (priznaky kvality se vazou na nej).
    /// </summary>
    public static string ChannelFor(string code)
        => code.Equals("pressureSeaLevel", StringComparison.OrdinalIgnoreCase) ? "pressure" : code;

    /// <summary>
    /// Vybere problemy pro velicinu (senzor + konkretni kanal). Vraci nejhorsi zavaznost a texty.
    /// </summary>
    public static (StationHealth? Severity, string Text) For(IReadOnlyList<SensorProblem> problems, string code)
    {
        string sensor = SensorFor(code);
        string channel = ChannelFor(code);
        List<SensorProblem> matching = problems
            .Where(problem => string.Equals(problem.Target, channel, StringComparison.OrdinalIgnoreCase)
                || (sensor.Length > 0 && string.Equals(problem.Target, sensor, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(problem => problem.Severity)
            .ToList();

        if (matching.Count == 0)
        {
            return (null, string.Empty);
        }

        string text = string.Join(", ", matching.Select(problem => problem.Text).Distinct().Take(2));
        return (matching[0].Severity, text);
    }
}
