namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Prepocet stanicniho tlaku na hladinu more (hypsometricka rovnice).
/// <para>
/// p0 = p · exp( g·h / (Rd · Tm) ),  Tm = T + 273,15 + γ·h/2
/// </para>
/// kde p je stanicni tlak [hPa], h nadmorska vyska cidla [m], T teplota vzduchu [°C],
/// g = 9,80665 m·s⁻², Rd = 287,05 J·kg⁻¹·K⁻¹ (suchy vzduch), γ = 0,0065 K·m⁻¹ (standardni teplotni gradient).
/// Tm je stredni teplota fiktivniho sloupce vzduchu mezi stanici a hladinou more.
/// Jde o zjednodusenou redukci (bez vlivu vlhkosti); ČHMÚ/WMO pouzivaji slozitejsi postup,
/// pro vysky kolem 250 m je rozdil v radu desetin hPa.
/// </summary>
public static class PressureReduction
{
    private const double Gravity = 9.80665;
    private const double DryAirGasConstant = 287.05;
    private const double LapseRate = 0.0065;
    private const double StandardTemperatureCelsius = 15.0;

    /// <summary>
    /// Vrati tlak prepocteny na hladinu more, nebo null, pokud chybi stanicni tlak.
    /// </summary>
    /// <param name="stationPressureHpa">Stanicni tlak [hPa].</param>
    /// <param name="temperatureCelsius">Teplota vzduchu [°C]; null nebo nepouzita => 15 °C (standardni atmosfera).</param>
    /// <param name="altitudeMeters">Nadmorska vyska cidla [m].</param>
    /// <param name="useMeasuredTemperature">False, pokud cidlo teploty neni venku (napr. stanice v interieru).</param>
    public static double? ToSeaLevel(double? stationPressureHpa, double? temperatureCelsius, double altitudeMeters, bool useMeasuredTemperature)
    {
        if (stationPressureHpa is null || double.IsNaN(stationPressureHpa.Value))
        {
            return null;
        }

        double temperature = useMeasuredTemperature && temperatureCelsius is { } measured && !double.IsNaN(measured)
            ? measured
            : StandardTemperatureCelsius;
        double meanColumnTemperatureKelvin = temperature + 273.15 + LapseRate * altitudeMeters / 2.0;
        return stationPressureHpa.Value * Math.Exp(Gravity * altitudeMeters / (DryAirGasConstant * meanColumnTemperatureKelvin));
    }
}
