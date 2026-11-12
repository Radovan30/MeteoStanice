namespace MeteoStanice.Monitor;

/// <summary>
/// Konfigurace monitorovaci aplikace (sekce "Monitor" v appsettings.json).
/// </summary>
public sealed class MonitorOptions
{
    /// <summary>
    /// Connection string do PostgreSQL (ConnectionStrings:MeteoDatabase).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Perioda prepocitani stari dat v sekundach (bez dotazu do DB) – kvuli detekci vypadku stanice.
    /// </summary>
    public int ReevaluateIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Nazev aplikace v zahlavi a v titulku okna.
    /// </summary>
    public string AppTitle { get; set; } = "Monitoring meteostanic";

    /// <summary>
    /// Zalozni nacteni z DB v sekundach, jen kdyz LISTEN/NOTIFY neni k dispozici (chybi trigger nebo spojeni).
    /// </summary>
    public int FallbackRefreshSeconds { get; set; } = 60;

    /// <summary>
    /// Pojistka: uplne nacteni z DB i pri fungujicich notifikacich (minuty). 0 = vypnuto.
    /// </summary>
    public int SafetyRefreshMinutes { get; set; } = 5;

    /// <summary>
    /// Kanal PostgreSQL LISTEN/NOTIFY (viz Database/monitor-notify.sql).
    /// </summary>
    public string NotifyChannel { get; set; } = "meteo_monitor_changed";

    /// <summary>
    /// Interval mereni pouzity, pokud stanice nema zaznam v StationSettings.
    /// </summary>
    public int DefaultMeasurementIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Pocet vynechanych intervalu, po kterem je stanice oznacena jako nemerici.
    /// </summary>
    public double OfflineAfterIntervals { get; set; } = 2.5;

    /// <summary>
    /// Jak daleko do minulosti se ctou udalosti zarizeni.
    /// </summary>
    public int EventLookbackHours { get; set; } = 24;

    /// <summary>
    /// Doba platnosti chybove udalosti, ktera nema parovou udalost zotaveni.
    /// </summary>
    public int TimeLimitedEventMinutes { get; set; } = 60;

    /// <summary>
    /// Jak dlouho po restartu stanice zobrazovat varovani.
    /// </summary>
    public int RestartWarningMinutes { get; set; } = 30;

    /// <summary>
    /// Nastaveni zvukoveho upozorneni.
    /// </summary>
    public SoundOptions Sound { get; set; } = new();

    /// <summary>
    /// Prepocet tlaku na hladinu more.
    /// </summary>
    public PressureReductionOptions PressureReduction { get; set; } = new();

    /// <summary>
    /// Zobrazovane veliciny znecisteni. Prazdny seznam = vychozi PM1/PM2,5/PM4/PM10.
    /// </summary>
    public List<PollutantOptions> Pollutants { get; set; } = [];

    /// <summary>
    /// Barvy stanic (poradi podle kodu stanice): prouzek karty, nazev a prepinac zobrazeni.
    /// </summary>
    public List<string> StationColors { get; set; } = [];

    /// <summary>
    /// Kulate ukazatele prostredi. Prazdny seznam = vychozi teplota/vlhkost/tlak.
    /// </summary>
    public List<EnvironmentGaugeOptions> EnvironmentGauges { get; set; } = [];

    /// <summary>
    /// Doplni vychozi hodnoty po nacteni konfigurace.
    /// </summary>
    public void ApplyDefaults()
    {
        ReevaluateIntervalSeconds = Math.Max(5, ReevaluateIntervalSeconds);
        FallbackRefreshSeconds = Math.Max(10, FallbackRefreshSeconds);
        if (string.IsNullOrWhiteSpace(NotifyChannel))
        {
            NotifyChannel = "meteo_monitor_changed";
        }

        if (string.IsNullOrWhiteSpace(AppTitle))
        {
            AppTitle = "Monitoring meteostanic";
        }

        if (StationColors.Count == 0)
        {
            StationColors.AddRange(["#4A90D9", "#EF8B3B", "#D2609A"]);
        }

        if (EnvironmentGauges.Count == 0)
        {
            EnvironmentGauges.AddRange(
            [
                new() { Code = "temperature", Label = "Teplota", Unit = "°C", Minimum = -20, Maximum = 50, Decimals = 1, Color = "#D32F2F" },
                new() { Code = "humidity", Label = "Vlhkost", Unit = "%", Minimum = 0, Maximum = 100, Decimals = 0, Color = "#1E88E5", WarningHigh = 85 },
                new() { Code = "pressureSeaLevel", Label = "Tlak (moře)", Unit = "hPa", Minimum = 960, Maximum = 1050, Decimals = 0, Color = "#2E7D32", Segments = 15 }
            ]);
        }

        DefaultMeasurementIntervalMinutes = Math.Max(1, DefaultMeasurementIntervalMinutes);
        OfflineAfterIntervals = Math.Max(1.0, OfflineAfterIntervals);
        Sound.RepeatSeconds = Math.Max(5, Sound.RepeatSeconds);

        if (Pollutants.Count == 0)
        {
            Pollutants.AddRange(
            [
                new() { Code = "pm1", Label = "PM1", Maximum = 100, WarningThreshold = 15, AlarmThreshold = 50, Color = "#8E24AA" },
                new() { Code = "pm25", Label = "PM2,5", Maximum = 100, WarningThreshold = 15, AlarmThreshold = 50, Color = "#3949AB" },
                new() { Code = "pm4", Label = "PM4", Maximum = 125, WarningThreshold = 30, AlarmThreshold = 75, Color = "#00ACC1" },
                new() { Code = "pm10", Label = "PM10", Maximum = 150, WarningThreshold = 45, AlarmThreshold = 100, Color = "#6D4C41" }
            ]);
        }
    }
}

/// <summary>
/// Nastaveni zvukoveho upozorneni.
/// </summary>
public sealed class SoundOptions
{
    /// <summary>
    /// Zvuk zapnut po startu aplikace.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Opakovani zvuku, dokud alarm neni potvrzen.
    /// </summary>
    public int RepeatSeconds { get; set; } = 60;

    /// <summary>
    /// Volitelny WAV soubor (relativne k adresari aplikace). Pokud chybi, pouzije se systemovy zvuk.
    /// </summary>
    public string? WavFile { get; set; } = "Sounds/alarm.wav";

    /// <summary>
    /// Spoustet alarm i pri prekroceni alarmoveho prahu znecisteni.
    /// </summary>
    public bool AlarmOnHighPollution { get; set; } = true;

    /// <summary>
    /// Spoustet alarm i pro varovani (nejen chyby a vypadky).
    /// </summary>
    public bool AlarmOnWarnings { get; set; }
}

/// <summary>
/// Definice jednoho progressbaru znecisteni.
/// </summary>
public sealed class PollutantOptions
{
    /// <summary>
    /// Kod veliciny: pm1, pm25, pm4, pm10, numberPm05, numberPm1, numberPm25, numberPm4, numberPm10.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Zobrazovany popisek.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Fyzikalni jednotka.
    /// </summary>
    public string Unit { get; set; } = "µg/m³";

    /// <summary>
    /// Hodnota odpovidajici plnemu progressbaru.
    /// </summary>
    public double Maximum { get; set; } = 100;

    /// <summary>
    /// Barva veliciny (hex). Uroven koncentrace se zobrazuje barevnym stitkem pod ukazatelem.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Prah zvysene koncentrace (orientacni).
    /// </summary>
    public double WarningThreshold { get; set; }

    /// <summary>
    /// Prah vysoke koncentrace (orientacni).
    /// </summary>
    public double AlarmThreshold { get; set; }
}

/// <summary>
/// Definice jednoho kulateho ukazatele (teplota, vlhkost, tlak).
/// </summary>
public sealed class EnvironmentGaugeOptions
{
    /// <summary>
    /// Kod veliciny: temperature, humidity, pressure, batteryVoltage, typicalParticleSize.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Zobrazovany popisek.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Fyzikalni jednotka.
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Hodnota na zacatku oblouku.
    /// </summary>
    public double Minimum { get; set; }

    /// <summary>
    /// Hodnota na konci oblouku.
    /// </summary>
    public double Maximum { get; set; } = 100;

    /// <summary>
    /// Pocet desetinnych mist.
    /// </summary>
    public int Decimals { get; set; } = 1;

    /// <summary>
    /// Barva oblouku (hex, napr. #D32F2F). Prazdne = vychozi modra.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Pocet dilku oblouku. 0 = plynuly oblouk, &gt; 0 = deleny oblouk.
    /// </summary>
    public int Segments { get; set; }

    /// <summary>
    /// Volitelny dolni prah upozorneni.
    /// </summary>
    public double? WarningLow { get; set; }

    /// <summary>
    /// Volitelny horni prah upozorneni.
    /// </summary>
    public double? WarningHigh { get; set; }
}

/// <summary>
/// Prepocet stanicniho tlaku na hladinu more.
/// </summary>
public sealed class PressureReductionOptions
{
    /// <summary>
    /// Vychozi nadmorska vyska cidla [m] pro stanice bez vlastniho zaznamu.
    /// </summary>
    public double DefaultAltitudeMeters { get; set; } = 242;

    /// <summary>
    /// Nadmorska vyska podle kodu stanice nebo textu umisteni (Stations.StationCode / Stations.LocationText).
    /// </summary>
    public Dictionary<string, double> StationAltitudes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pouzit namerenou teplotu stanice (true) nebo standardnich 15 °C (false, napr. pri mereni v interieru).
    /// </summary>
    public bool UseMeasuredTemperature { get; set; } = true;

    /// <summary>
    /// Najde nadmorskou vysku stanice: nejdrive podle kodu, pak podle umisteni, jinak vychozi.
    /// </summary>
    public double AltitudeFor(string? stationCode, string? locationText)
    {
        foreach ((string key, double altitude) in StationAltitudes)
        {
            if (string.Equals(key.Trim(), stationCode?.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(key.Trim(), locationText?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return altitude;
            }
        }

        return DefaultAltitudeMeters;
    }
}
