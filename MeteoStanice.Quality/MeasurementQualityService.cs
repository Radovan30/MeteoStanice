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
        CheckRange(flags, "pm1", measurement.Pm1, 0, 500, "PM1.0 je mimo ocekavany rozsah.");
        CheckRange(flags, "pm25", measurement.Pm25, 0, 500, "PM2.5 je mimo ocekavany rozsah.");
        CheckRange(flags, "pm4", measurement.Pm4, 0, 600, "PM4.0 je mimo ocekavany rozsah.");
        CheckRange(flags, "pm10", measurement.Pm10, 0, 700, "PM10 je mimo ocekavany rozsah.");
        CheckRange(flags, "numberPm05", measurement.NumberPm05, 0, 100000, "Pocetni koncentrace PM0.5 je mimo ocekavany rozsah.");
        CheckRange(flags, "numberPm1", measurement.NumberPm1, 0, 100000, "Pocetni koncentrace PM1.0 je mimo ocekavany rozsah.");
        CheckRange(flags, "numberPm25", measurement.NumberPm25, 0, 100000, "Pocetni koncentrace PM2.5 je mimo ocekavany rozsah.");
        CheckRange(flags, "numberPm4", measurement.NumberPm4, 0, 100000, "Pocetni koncentrace PM4.0 je mimo ocekavany rozsah.");
        CheckRange(flags, "numberPm10", measurement.NumberPm10, 0, 100000, "Pocetni koncentrace PM10 je mimo ocekavany rozsah.");
        CheckRange(flags, "typicalParticleSize", measurement.TypicalParticleSize, 0, 20, "Typicka velikost castic je mimo ocekavany rozsah.");
        CheckCompleteness(flags, "sht40", measurement.Sht40OkCount, measurement.ExpectedSampleCount, "SHT40 ma malo platnych vzorku v intervalu.");
        CheckCompleteness(flags, "bmp280", measurement.Bmp280OkCount, measurement.ExpectedSampleCount, "BMP280 ma malo platnych vzorku v intervalu.");
        if (!string.Equals(measurement.PowerSource, "BAT", StringComparison.OrdinalIgnoreCase))
        {
            CheckCompleteness(flags, "sps30", measurement.Sps30OkCount, measurement.ExpectedSampleCount, "SPS30 ma malo platnych vzorku v intervalu.");
        }

        CheckFlatline(flags, "temperature", measurement.TemperatureCelsius, measurement.TemperatureStdDev, measurement.Sht40OkCount, 0.001, "Teplota je v intervalu podezrele konstantni.");
        CheckFlatline(flags, "humidity", measurement.HumidityPercent, measurement.HumidityStdDev, measurement.Sht40OkCount, 0.001, "Vlhkost je v intervalu podezrele konstantni.");
        CheckFlatline(flags, "pressure", measurement.PressureHpa, measurement.PressureStdDev, measurement.Bmp280OkCount, 0.001, "Tlak je v intervalu podezrele konstantni.");
        CheckFlatline(flags, "pm25", measurement.Pm25, measurement.Pm25StdDev, measurement.Sps30OkCount, 0.001, "PM2.5 je v intervalu podezrele konstantni.");
        CheckFlatline(flags, "pm10", measurement.Pm10, measurement.Pm10StdDev, measurement.Sps30OkCount, 0.001, "PM10 je v intervalu podezrele konstantni.");
        CheckParticleOrder(flags, measurement);

        if (previousMeasurement is not null)
        {
            CheckJump(flags, "temperature", measurement.TemperatureCelsius, previousMeasurement.TemperatureCelsius, 8, "Teplota se zmenila skokove proti predchozimu mereni.");
            CheckJump(flags, "humidity", measurement.HumidityPercent, previousMeasurement.HumidityPercent, 30, "Vlhkost se zmenila skokove proti predchozimu mereni.");
            CheckJump(flags, "pm1", measurement.Pm1, previousMeasurement.Pm1, 150, "PM1.0 se zmenilo skokove proti predchozimu mereni.");
            CheckJump(flags, "pm25", measurement.Pm25, previousMeasurement.Pm25, 150, "PM2.5 se zmenilo skokove proti predchozimu mereni.");
            CheckJump(flags, "pm4", measurement.Pm4, previousMeasurement.Pm4, 180, "PM4.0 se zmenilo skokove proti predchozimu mereni.");
            CheckJump(flags, "pm10", measurement.Pm10, previousMeasurement.Pm10, 200, "PM10 se zmenilo skokove proti predchozimu mereni.");
            CheckRepeatedConstant(flags, "temperature", measurement.TemperatureCelsius, previousMeasurement.TemperatureCelsius, measurement.TemperatureStdDev, previousMeasurement.TemperatureStdDev, 0.001, "Teplota je podezrele konstantni i proti predchozimu intervalu.");
            CheckRepeatedConstant(flags, "humidity", measurement.HumidityPercent, previousMeasurement.HumidityPercent, measurement.HumidityStdDev, previousMeasurement.HumidityStdDev, 0.001, "Vlhkost je podezrele konstantni i proti predchozimu intervalu.");
            CheckRepeatedConstant(flags, "pm25", measurement.Pm25, previousMeasurement.Pm25, measurement.Pm25StdDev, previousMeasurement.Pm25StdDev, 0.001, "PM2.5 je podezrele konstantni i proti predchozimu intervalu.");
            CheckRepeatedConstant(flags, "pm10", measurement.Pm10, previousMeasurement.Pm10, measurement.Pm10StdDev, previousMeasurement.Pm10StdDev, 0.001, "PM10 je podezrele konstantni i proti predchozimu intervalu.");
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

    private static void CheckCompleteness(List<QualityFlagCandidate> flags, string channelCode, int okCount, int expectedCount, string message)
    {
        if (expectedCount <= 0)
        {
            return;
        }

        if (okCount * 100 < expectedCount * 80)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = channelCode,
                CheckCode = "LowCompleteness",
                Severity = "Warning",
                Message = message
            });
        }
    }

    private static void CheckFlatline(List<QualityFlagCandidate> flags, string channelCode, double? mean, double? standardDeviation, int sampleCount, double threshold, string message)
    {
        if (mean is null || standardDeviation is null || sampleCount < 5)
        {
            return;
        }

        if (Math.Abs(mean.Value) > 0.1 && standardDeviation.Value <= threshold)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = channelCode,
                CheckCode = "Flatline",
                Severity = "Warning",
                Message = message
            });
        }
    }

    private static void CheckRepeatedConstant(List<QualityFlagCandidate> flags, string channelCode, double? currentValue, double? previousValue, double? currentStdDev, double? previousStdDev, double threshold, string message)
    {
        if (currentValue is null || previousValue is null || currentStdDev is null || previousStdDev is null)
        {
            return;
        }

        if (Math.Abs(currentValue.Value - previousValue.Value) <= threshold
            && currentStdDev.Value <= threshold
            && previousStdDev.Value <= threshold)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = channelCode,
                CheckCode = "RepeatedConstant",
                Severity = "Warning",
                Message = message
            });
        }
    }

    private static void CheckParticleOrder(List<QualityFlagCandidate> flags, Measurement measurement)
    {
        if (measurement.Pm1 is null || measurement.Pm25 is null || measurement.Pm4 is null || measurement.Pm10 is null)
        {
            return;
        }

        if (measurement.Pm1 > measurement.Pm25 || measurement.Pm25 > measurement.Pm4 || measurement.Pm4 > measurement.Pm10)
        {
            flags.Add(new QualityFlagCandidate
            {
                ChannelCode = "sps30",
                CheckCode = "ParticleMassOrder",
                Severity = "Warning",
                Message = "Poradi hmotnostnich koncentraci PM1/PM2.5/PM4/PM10 je fyzikalne podezrele."
            });
        }
    }
}
