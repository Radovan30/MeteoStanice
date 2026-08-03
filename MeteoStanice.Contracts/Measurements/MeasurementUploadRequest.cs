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
    /// Human readable station code used by operators.
    /// </summary>
    public string StationCode { get; set; } = string.Empty;

    /// <summary>
    /// Station-local monotonically increasing measurement sequence.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// UTC timestamp when the measurement interval was closed.
    /// </summary>
    public DateTimeOffset MeasuredAt { get; set; }

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
    /// Average PM2.5 concentration for the interval.
    /// </summary>
    public double? Pm25 { get; set; }

    /// <summary>
    /// Average PM10 concentration for the interval.
    /// </summary>
    public double? Pm10 { get; set; }

    /// <summary>
    /// Battery voltage measured by the station when available.
    /// </summary>
    public double? BatteryVoltage { get; set; }

    /// <summary>
    /// Power source reported by the station, for example BAT or SRC.
    /// </summary>
    public string PowerSource { get; set; } = string.Empty;

    /// <summary>
    /// Firmware status string for the produced record.
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
