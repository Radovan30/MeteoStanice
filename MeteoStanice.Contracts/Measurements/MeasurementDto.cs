namespace MeteoStanice.Contracts.Measurements;

/// <summary>
/// Measurement returned by the API to dashboards and clients.
/// </summary>
public sealed class MeasurementDto
{
    /// <summary>
    /// Database identifier of the measurement.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Stable GUID of the station that produced the measurement.
    /// </summary>
    public Guid StationGuid { get; set; }

    /// <summary>
    /// Human-readable station code assigned by the backend.
    /// </summary>
    public string StationCode { get; set; } = string.Empty;

    /// <summary>
    /// Display name assigned to the station.
    /// </summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>
    /// Station-local monotonically increasing measurement sequence.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// UTC timestamp when the measurement interval was closed.
    /// </summary>
    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>
    /// Average temperature in degrees Celsius.
    /// </summary>
    public double? TemperatureCelsius { get; set; }

    /// <summary>
    /// Average relative humidity in percent.
    /// </summary>
    public double? HumidityPercent { get; set; }

    /// <summary>
    /// Average atmospheric pressure in hPa.
    /// </summary>
    public double? PressureHpa { get; set; }

    /// <summary>
    /// Average PM1.0 mass concentration.
    /// </summary>
    public double? Pm1 { get; set; }

    /// <summary>
    /// Average PM2.5 concentration.
    /// </summary>
    public double? Pm25 { get; set; }

    /// <summary>
    /// Average PM4.0 mass concentration.
    /// </summary>
    public double? Pm4 { get; set; }

    /// <summary>
    /// Average PM10 concentration.
    /// </summary>
    public double? Pm10 { get; set; }

    /// <summary>
    /// Average number concentration for particles larger than 0.5 um.
    /// </summary>
    public double? NumberPm05 { get; set; }

    /// <summary>
    /// Average number concentration for particles larger than 1.0 um.
    /// </summary>
    public double? NumberPm1 { get; set; }

    /// <summary>
    /// Average number concentration for particles larger than 2.5 um.
    /// </summary>
    public double? NumberPm25 { get; set; }

    /// <summary>
    /// Average number concentration for particles larger than 4.0 um.
    /// </summary>
    public double? NumberPm4 { get; set; }

    /// <summary>
    /// Average number concentration for particles larger than 10 um.
    /// </summary>
    public double? NumberPm10 { get; set; }

    /// <summary>
    /// Average typical particle size reported by SPS30.
    /// </summary>
    public double? TypicalParticleSize { get; set; }

    /// <summary>
    /// Power source reported or inferred for the measurement.
    /// </summary>
    public string PowerSource { get; set; } = string.Empty;

    /// <summary>
    /// Number of samples expected in the closed interval.
    /// </summary>
    public int ExpectedSampleCount { get; set; }

    /// <summary>
    /// Number of samples actually processed in the closed interval.
    /// </summary>
    public int ReceivedSampleCount { get; set; }

    /// <summary>
    /// Successful SHT40 reads in the interval.
    /// </summary>
    public int Sht40OkCount { get; set; }

    /// <summary>
    /// Failed SHT40 reads in the interval.
    /// </summary>
    public int Sht40ErrorCount { get; set; }

    /// <summary>
    /// Successful BMP280 reads in the interval.
    /// </summary>
    public int Bmp280OkCount { get; set; }

    /// <summary>
    /// Failed BMP280 reads in the interval.
    /// </summary>
    public int Bmp280ErrorCount { get; set; }

    /// <summary>
    /// Successful SPS30 reads in the interval.
    /// </summary>
    public int Sps30OkCount { get; set; }

    /// <summary>
    /// SPS30 reads where data was not ready yet.
    /// </summary>
    public int Sps30WaitCount { get; set; }

    /// <summary>
    /// Failed SPS30 reads in the interval.
    /// </summary>
    public int Sps30ErrorCount { get; set; }

    /// <summary>
    /// SPS30 reads skipped because the station ran on battery.
    /// </summary>
    public int Sps30BatterySkippedCount { get; set; }

    /// <summary>
    /// Minimum temperature observed in the interval.
    /// </summary>
    public double? TemperatureMin { get; set; }

    /// <summary>
    /// Maximum temperature observed in the interval.
    /// </summary>
    public double? TemperatureMax { get; set; }

    /// <summary>
    /// Temperature standard deviation in the interval.
    /// </summary>
    public double? TemperatureStdDev { get; set; }

    /// <summary>
    /// Minimum humidity observed in the interval.
    /// </summary>
    public double? HumidityMin { get; set; }

    /// <summary>
    /// Maximum humidity observed in the interval.
    /// </summary>
    public double? HumidityMax { get; set; }

    /// <summary>
    /// Humidity standard deviation in the interval.
    /// </summary>
    public double? HumidityStdDev { get; set; }

    /// <summary>
    /// Minimum pressure observed in the interval.
    /// </summary>
    public double? PressureMin { get; set; }

    /// <summary>
    /// Maximum pressure observed in the interval.
    /// </summary>
    public double? PressureMax { get; set; }

    /// <summary>
    /// Pressure standard deviation in the interval.
    /// </summary>
    public double? PressureStdDev { get; set; }

    /// <summary>
    /// Minimum PM2.5 observed in the interval.
    /// </summary>
    public double? Pm25Min { get; set; }

    /// <summary>
    /// Maximum PM2.5 observed in the interval.
    /// </summary>
    public double? Pm25Max { get; set; }

    /// <summary>
    /// PM2.5 standard deviation in the interval.
    /// </summary>
    public double? Pm25StdDev { get; set; }

    /// <summary>
    /// Minimum PM10 observed in the interval.
    /// </summary>
    public double? Pm10Min { get; set; }

    /// <summary>
    /// Maximum PM10 observed in the interval.
    /// </summary>
    public double? Pm10Max { get; set; }

    /// <summary>
    /// PM10 standard deviation in the interval.
    /// </summary>
    public double? Pm10StdDev { get; set; }

    /// <summary>
    /// Quality flag count attached to this measurement.
    /// </summary>
    public int QualityFlagCount { get; set; }
}
