using MeteoStanice.Data.Entities;

namespace MeteoStanice.Quality;

/// <summary>
/// Runs first-pass rule-based quality checks for one accepted measurement.
/// </summary>
public sealed class MeasurementQualityService
{
    /// <summary>
    /// Evaluates simple quality checks for one measurement.
    /// </summary>
    public IReadOnlyList<QualityFlagCandidate> Evaluate(Measurement measurement, Measurement? previousMeasurement)
    {
        List<QualityFlagCandidate> flags = [];

        CheckRange(flags, "temperature", measurement.TemperatureCelsius, -40, 70, "Teplota je mimo fyzicky ocekavany rozsah.");
        CheckRange(flags, "humidity", measurement.HumidityPercent, 0, 100, "Vlhkost je mimo rozsah 0-100 %.");
        CheckRange(flags, "pressure", measurement.PressureHpa, 850, 1100, "Tlak je mimo ocekavany rozsah.");
        CheckRange(flags, "pm25", measurement.Pm25, 0, 500, "PM2.5 je mimo ocekavany rozsah.");
        CheckRange(flags, "pm10", measurement.Pm10, 0, 700, "PM10 je mimo ocekavany rozsah.");

        if (previousMeasurement is not null)
        {
            CheckJump(flags, "temperature", measurement.TemperatureCelsius, previousMeasurement.TemperatureCelsius, 8, "Teplota se zmenila skokove proti predchozimu mereni.");
            CheckJump(flags, "humidity", measurement.HumidityPercent, previousMeasurement.HumidityPercent, 30, "Vlhkost se zmenila skokove proti predchozimu mereni.");
            CheckJump(flags, "pm25", measurement.Pm25, previousMeasurement.Pm25, 150, "PM2.5 se zmenilo skokove proti predchozimu mereni.");
            CheckJump(flags, "pm10", measurement.Pm10, previousMeasurement.Pm10, 200, "PM10 se zmenilo skokove proti predchozimu mereni.");
        }

        if (string.Equals(measurement.PowerSource, "BAT", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = "batteryVoltage",
                CheckCode = "BatteryContext",
                Severity = "Information",
                Message = "Mereni bylo porizeno pri behu na baterii."
            });
        }

        return flags;
    }

    private static void CheckRange(List<QualityFlagCandidate> flags, string channelCode, double? value, double min, double max, string message)
    {
        if (value is null)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = channelCode,
                CheckCode = "MissingValue",
                Severity = "Warning",
                Message = "Hodnota chybi."
            });
            return;
        }

        if (value < min || value > max)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = channelCode,
                CheckCode = "OutOfRange",
                Severity = "Warning",
                Message = message
            });
        }
    }

    private static void CheckJump(List<QualityFlagCandidate> flags, string channelCode, double? currentValue, double? previousValue, double threshold, string message)
    {
        if (currentValue is null || previousValue is null)
        {
            return;
        }

        if (Math.Abs(currentValue.Value - previousValue.Value) >= threshold)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = channelCode,
                CheckCode = "SuddenJump",
                Severity = "Warning",
                Message = message
            });
        }
    }
}
