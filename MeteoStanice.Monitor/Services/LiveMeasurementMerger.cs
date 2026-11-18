using MeteoStanice.Contracts.Samples;
using MeteoStanice.Data.Entities;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Builds the values shown on the gauges: the latest 10 s sample where the sensor read was OK,
/// otherwise the 10 min aggregate. Interval min/max, sample counts and status stay from the aggregate.
/// The result is a detached display object; nothing is written anywhere.
/// </summary>
public static class LiveMeasurementMerger
{
    public static Measurement? Merge(Measurement? aggregate, SensorSampleDto? live, double sps30CorrectionPercent)
    {
        if (live is null)
        {
            return aggregate;
        }

        Measurement display = aggregate is null ? new Measurement() : Copy(aggregate);
        if (aggregate is null)
        {
            display.MeasuredAt = live.MeasuredAt;
        }

        if (IsOk(live.Sht40Status))
        {
            display.TemperatureCelsius = live.TemperatureCelsius;
            display.HumidityPercent = live.HumidityPercent;
        }

        if (IsOk(live.Bmp280Status))
        {
            display.PressureHpa = live.PressureHpa;
        }

        if (IsOk(live.Sps30Status))
        {
            double factor = 1 + sps30CorrectionPercent / 100.0;
            display.Pm1 = live.Pm1 * factor;
            display.Pm25 = live.Pm25 * factor;
            display.Pm4 = live.Pm4 * factor;
            display.Pm10 = live.Pm10 * factor;
            display.NumberPm05 = live.NumberPm05;
            display.NumberPm1 = live.NumberPm1;
            display.NumberPm25 = live.NumberPm25;
            display.NumberPm4 = live.NumberPm4;
            display.NumberPm10 = live.NumberPm10;
            display.TypicalParticleSize = live.TypicalParticleSize;
        }

        if (live.BatteryVoltage is not null)
        {
            display.BatteryVoltage = live.BatteryVoltage;
        }

        if (!string.IsNullOrWhiteSpace(live.PowerSource))
        {
            display.PowerSource = live.PowerSource;
        }

        return display;
    }

    private static bool IsOk(string? status)
        => string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "None", StringComparison.OrdinalIgnoreCase);

    private static Measurement Copy(Measurement source) => new()
    {
        Id = source.Id,
        StationId = source.StationId,
        SequenceNumber = source.SequenceNumber,
        MeasuredAt = source.MeasuredAt,
        CreatedAt = source.CreatedAt,
        TemperatureCelsius = source.TemperatureCelsius,
        HumidityPercent = source.HumidityPercent,
        PressureHpa = source.PressureHpa,
        Pm1 = source.Pm1,
        Pm25 = source.Pm25,
        Pm4 = source.Pm4,
        Pm10 = source.Pm10,
        NumberPm05 = source.NumberPm05,
        NumberPm1 = source.NumberPm1,
        NumberPm25 = source.NumberPm25,
        NumberPm4 = source.NumberPm4,
        NumberPm10 = source.NumberPm10,
        TypicalParticleSize = source.TypicalParticleSize,
        BatteryVoltage = source.BatteryVoltage,
        PowerSource = source.PowerSource,
        ExpectedSampleCount = source.ExpectedSampleCount,
        ReceivedSampleCount = source.ReceivedSampleCount,
        Sht40OkCount = source.Sht40OkCount,
        Sht40ErrorCount = source.Sht40ErrorCount,
        Bmp280OkCount = source.Bmp280OkCount,
        Bmp280ErrorCount = source.Bmp280ErrorCount,
        Sps30OkCount = source.Sps30OkCount,
        Sps30WaitCount = source.Sps30WaitCount,
        Sps30ErrorCount = source.Sps30ErrorCount,
        Sps30BatterySkippedCount = source.Sps30BatterySkippedCount,
        TemperatureMin = source.TemperatureMin,
        TemperatureMax = source.TemperatureMax,
        TemperatureStdDev = source.TemperatureStdDev,
        HumidityMin = source.HumidityMin,
        HumidityMax = source.HumidityMax,
        HumidityStdDev = source.HumidityStdDev,
        PressureMin = source.PressureMin,
        PressureMax = source.PressureMax,
        PressureStdDev = source.PressureStdDev,
        Pm25Min = source.Pm25Min,
        Pm25Max = source.Pm25Max,
        Pm25StdDev = source.Pm25StdDev,
        Pm10Min = source.Pm10Min,
        Pm10Max = source.Pm10Max,
        Pm10StdDev = source.Pm10StdDev,
        Status = source.Status
    };
}
