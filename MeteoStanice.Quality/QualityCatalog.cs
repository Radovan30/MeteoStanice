namespace MeteoStanice.Quality;

/// <summary>
/// Human-readable names and units for quality channels and descriptions of quality checks,
/// used when the backend creates the SensorChannels and QualityChecks lookup rows.
/// </summary>
public static class QualityCatalog
{
    private static readonly Dictionary<string, (string Name, string Unit)> Channels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temperature"] = ("Teplota SHT40", "°C"),
        ["humidity"] = ("Relativní vlhkost SHT40", "%"),
        ["pressure"] = ("Tlak BMP280", "hPa"),
        ["pm1"] = ("PM1,0 SPS30", "µg/m³"),
        ["pm25"] = ("PM2,5 SPS30", "µg/m³"),
        ["pm4"] = ("PM4,0 SPS30", "µg/m³"),
        ["pm10"] = ("PM10 SPS30", "µg/m³"),
        ["typicalParticleSize"] = ("Typická velikost částic SPS30", "µm"),
        ["sht40"] = ("Senzor SHT40", string.Empty),
        ["bmp280"] = ("Senzor BMP280", string.Empty),
        ["sps30"] = ("Senzor SPS30", string.Empty),
        ["batteryVoltage"] = ("Napětí baterie", "V")
    };

    private static readonly Dictionary<string, string> Checks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MissingValue"] = "Hodnota v intervalu chybí (žádné platné čtení).",
        ["OutOfRange"] = "Průměr, minimum nebo maximum intervalu je mimo fyzikálně očekávaný rozsah.",
        ["LowCompleteness"] = "Méně než 80 % očekávaných platných vzorků senzoru v intervalu.",
        ["Flatline"] = "Směrodatná odchylka intervalu je pod rozlišením senzoru (hodnoty se nemění).",
        ["SuddenJump"] = "Skoková změna průměru proti bezprostředně předchozímu intervalu.",
        ["RepeatedConstant"] = "Stejný konstantní průměr ve dvou po sobě jdoucích intervalech.",
        ["ParticleMassOrder"] = "Porušené pořadí PM1 ≤ PM2,5 ≤ PM4 ≤ PM10 (indikuje poškozená data).",
        ["BatteryContext"] = "Informace: interval proběhl alespoň částečně na baterii."
    };

    public static (string Name, string Unit) DescribeChannel(string code)
        => Channels.TryGetValue(code, out (string Name, string Unit) value) ? value : (code, string.Empty);

    public static string DescribeCheck(string code)
        => Checks.TryGetValue(code, out string? value) ? value : string.Empty;
}
