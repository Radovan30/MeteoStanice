namespace MeteoStanice.Contracts.Measurements;

/// <summary>
/// Measurement payload sent by a station firmware to the backend API.
/// </summary>
public sealed class MeasurementUploadRequest
{
    /// <summary>
    /// Stable station GUID generated and stored by the firmware.
    /// </summary>
    public Guid StationGuid { get; set; }

    /// <summary>
    /// Legacy station identifier used by the current nanoFramework firmware.
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Human readable station code used by operators.
    /// </summary>
    public string StationCode { get; set; } = string.Empty;

    /// <summary>
    /// Physical board identity reported by firmware.
    /// </summary>
    public string HardwareId { get; set; } = string.Empty;

    /// <summary>
    /// Firmware version reported by the station.
    /// </summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>
    /// UTC interval timestamp encoded as yyyyMMddHHmmss.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// UTC timestamp when the measurement interval was closed.
    /// </summary>
    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>
    /// Legacy timestamp name used by the current nanoFramework firmware.
    /// </summary>
    public DateTimeOffset SampledAt { get; set; }

    /// <summary>
    /// Average temperature in degrees Celsius for the interval.
    /// </summary>
    public double? TemperatureCelsius { get; set; }

    /// <summary>
    /// Average relative humidity in percent for the interval.
    /// </summary>
    public double? HumidityPercent { get; set; }

    /// <summary>
    /// Average atmospheric pressure in hPa for the interval.
    /// </summary>
    public double? PressureHpa { get; set; }

    /// <summary>
    /// Average PM1.0 mass concentration for the interval.
    /// </summary>
    public double? Pm1 { get; set; }

    /// <summary>
    /// Average PM2.5 concentration for the interval.
    /// </summary>
    public double? Pm25 { get; set; }

    /// <summary>
    /// Average PM4.0 mass concentration for the interval.
    /// </summary>
    public double? Pm4 { get; set; }

    /// <summary>
    /// Average PM10 concentration for the interval.
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
    /// Battery voltage measured by the station when available.
    /// </summary>
    public double? BatteryVoltage { get; set; }

    /// <summary>
    /// Power source reported by the station, for example BAT or SRC.
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
    /// Firmware status string for the produced record.
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
