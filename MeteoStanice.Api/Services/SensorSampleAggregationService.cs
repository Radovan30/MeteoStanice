using System.Globalization;
using MeteoStanice.Api.Hubs;
using MeteoStanice.Contracts.Measurements;
using MeteoStanice.Contracts.Statuses;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using MeteoStanice.Quality;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MeteoStanice.Api.Services;

/// <summary>
/// Rebuilds completed ten-minute measurements from the authoritative raw samples.
/// </summary>
public sealed class SensorSampleAggregationService(
    MeteoDbContext dbContext,
    MeasurementQualityService qualityService,
    IHubContext<MeteoHub> meteoHubContext)
{
    private const long AggregationLockKey = 0x4D4554454F414747L;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private const int SamplePeriodSeconds = 10;
    private const int ExpectedSampleCount = (10 * 60) / SamplePeriodSeconds;

    public async Task RebuildAffectedIntervalsAsync(
        Station station,
        IEnumerable<DateTimeOffset> sampleTimes,
        CancellationToken ct)
    {
        // Only a sample of an already closed interval (late delivery from the flash queue) changes a
        // stored aggregate: rebuild its interval and the following one (whose jump check compares with it).
        // A sample of the still open interval needs no rebuild; the periodic worker closes the interval.
        // With one upload every 10 s per station this avoids a rebuild transaction on every request.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HashSet<DateTimeOffset> intervalStarts = [];
        foreach (DateTimeOffset sampleTime in sampleTimes)
        {
            DateTimeOffset currentStart = AlignIntervalStart(sampleTime);
            if (currentStart.Add(Interval) > now)
            {
                continue;
            }

            intervalStarts.Add(currentStart);
            intervalStarts.Add(currentStart.Add(Interval));
        }

        foreach (DateTimeOffset intervalStart in intervalStarts.OrderBy(value => value))
        {
            DateTimeOffset intervalEnd = intervalStart.Add(Interval);
            if (intervalEnd > now)
            {
                continue;
            }

            MeasurementDto? dto = await RebuildIntervalAsync(station, intervalStart, intervalEnd, ct);
            if (dto is null)
            {
                continue;
            }

            await meteoHubContext.Clients.All.SendAsync(
                MeteoHub.MeasurementReceivedMethod,
                dto,
                ct);
        }
    }

    private async Task<MeasurementDto?> RebuildIntervalAsync(
        Station station,
        DateTimeOffset intervalStart,
        DateTimeOffset intervalEnd,
        CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AggregationLockKey})",
            ct);

        List<SensorSample> samples = await dbContext.SensorSamples
                .AsNoTracking()
                .Where(item => item.StationId == station.Id
                    && item.MeasuredAt >= intervalStart
                    && item.MeasuredAt < intervalEnd)
                .OrderBy(item => item.MeasuredAt)
                .ToListAsync(ct);

        if (samples.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return null;
        }

        long sequenceNumber = CreateSequenceNumber(intervalEnd);
        Measurement? measurement = await dbContext.Measurements
                .Include(item => item.QualityFlags)
                .SingleOrDefaultAsync(item => item.StationId == station.Id
                    && item.SequenceNumber == sequenceNumber, ct);

        bool isNew = measurement is null;
        int previousSampleCount = measurement?.ReceivedSampleCount ?? 0;
        if (measurement is null)
        {
            measurement = new Measurement
            {
                Station = station,
                SequenceNumber = sequenceNumber,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Measurements.Add(measurement);
        }
        else
        {
            dbContext.MeasurementQualityFlags.RemoveRange(measurement.QualityFlags);
            measurement.QualityFlags.Clear();
        }

        ApplyAggregate(measurement, samples, intervalEnd);

        // Jump and repeated-constant checks compare only with the directly preceding interval;
        // after an outage there is no meaningful "previous" value.
        Measurement? previousMeasurement = await dbContext.Measurements
                .AsNoTracking()
                .Where(item => item.StationId == station.Id && item.MeasuredAt == intervalStart)
                .FirstOrDefaultAsync(ct);

        IReadOnlyList<QualityFlagCandidate> flags = qualityService.Evaluate(measurement, previousMeasurement);
        int storedFlagCount = await AddQualityFlagsAsync(measurement, flags, ct);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Notify clients only about a new interval or an interval that received new samples.
        bool changed = isNew || previousSampleCount != measurement.ReceivedSampleCount;
        return changed ? MeasurementMapper.ToDto(measurement, station, storedFlagCount) : null;
    }

    private static void ApplyAggregate(
        Measurement measurement,
        IReadOnlyList<SensorSample> samples,
        DateTimeOffset intervalEnd)
    {
        Statistics temperature = Statistics.From(samples.Select(item => item.TemperatureCelsius));
        Statistics humidity = Statistics.From(samples.Select(item => item.HumidityPercent));
        Statistics pressure = Statistics.From(samples.Select(item => item.PressureHpa));
        Statistics pm1 = Statistics.From(samples.Select(item => item.Pm1));
        Statistics pm25 = Statistics.From(samples.Select(item => item.Pm25));
        Statistics pm4 = Statistics.From(samples.Select(item => item.Pm4));
        Statistics pm10 = Statistics.From(samples.Select(item => item.Pm10));
        Statistics numberPm05 = Statistics.From(samples.Select(item => item.NumberPm05));
        Statistics numberPm1 = Statistics.From(samples.Select(item => item.NumberPm1));
        Statistics numberPm25 = Statistics.From(samples.Select(item => item.NumberPm25));
        Statistics numberPm4 = Statistics.From(samples.Select(item => item.NumberPm4));
        Statistics numberPm10 = Statistics.From(samples.Select(item => item.NumberPm10));
        Statistics particleSize = Statistics.From(samples.Select(item => item.TypicalParticleSize));
        Statistics batteryVoltage = Statistics.From(samples.Select(item => item.BatteryVoltage));

        int sht40Ok = samples.Count(item => IsOk(item.Sht40Status));
        int bmp280Ok = samples.Count(item => IsOk(item.Bmp280Status));
        int sps30Ok = samples.Count(item => IsOk(item.Sps30Status));
        int sps30Wait = samples.Count(item => IsStatus(item.Sps30Status, nameof(SensorErrorCode.Timeout)));
        int sps30BatterySkipped = samples.Count(item => IsStatus(item.Sps30Status, nameof(SensorErrorCode.DisabledOnBattery)));

        measurement.MeasuredAt = intervalEnd;
        measurement.TemperatureCelsius = temperature.Mean;
        measurement.HumidityPercent = humidity.Mean;
        measurement.PressureHpa = pressure.Mean;
        measurement.Pm1 = pm1.Mean;
        measurement.Pm25 = pm25.Mean;
        measurement.Pm4 = pm4.Mean;
        measurement.Pm10 = pm10.Mean;
        measurement.NumberPm05 = numberPm05.Mean;
        measurement.NumberPm1 = numberPm1.Mean;
        measurement.NumberPm25 = numberPm25.Mean;
        measurement.NumberPm4 = numberPm4.Mean;
        measurement.NumberPm10 = numberPm10.Mean;
        measurement.TypicalParticleSize = particleSize.Mean;
        measurement.BatteryVoltage = batteryVoltage.Mean;
        measurement.PowerSource = samples.Any(item => string.Equals(item.PowerSource, "BAT", StringComparison.OrdinalIgnoreCase))
            ? "BAT"
            : "SRC";
        measurement.ExpectedSampleCount = ExpectedSampleCount;
        measurement.ReceivedSampleCount = samples.Count;
        measurement.Sht40OkCount = sht40Ok;
        measurement.Sht40ErrorCount = samples.Count - sht40Ok;
        measurement.Bmp280OkCount = bmp280Ok;
        measurement.Bmp280ErrorCount = samples.Count - bmp280Ok;
        measurement.Sps30OkCount = sps30Ok;
        measurement.Sps30WaitCount = sps30Wait;
        measurement.Sps30BatterySkippedCount = sps30BatterySkipped;
        measurement.Sps30ErrorCount = samples.Count - sps30Ok - sps30Wait - sps30BatterySkipped;
        measurement.TemperatureMin = temperature.Minimum;
        measurement.TemperatureMax = temperature.Maximum;
        measurement.TemperatureStdDev = temperature.StandardDeviation;
        measurement.HumidityMin = humidity.Minimum;
        measurement.HumidityMax = humidity.Maximum;
        measurement.HumidityStdDev = humidity.StandardDeviation;
        measurement.PressureMin = pressure.Minimum;
        measurement.PressureMax = pressure.Maximum;
        measurement.PressureStdDev = pressure.StandardDeviation;
        measurement.Pm25Min = pm25.Minimum;
        measurement.Pm25Max = pm25.Maximum;
        measurement.Pm25StdDev = pm25.StandardDeviation;
        measurement.Pm10Min = pm10.Minimum;
        measurement.Pm10Max = pm10.Maximum;
        measurement.Pm10StdDev = pm10.StandardDeviation;
        measurement.Status = ResolveStatus(measurement);
    }

    private static string ResolveStatus(Measurement measurement)
    {
        if (measurement.Sht40ErrorCount > 0) return nameof(MeasurementStatusCode.Sht40ReadFailed);
        if (measurement.Bmp280ErrorCount > 0) return nameof(MeasurementStatusCode.Bmp280ReadFailed);
        if (measurement.Sps30ErrorCount > 0) return nameof(MeasurementStatusCode.Sps30ReadFailed);
        if (measurement.Sps30BatterySkippedCount > 0) return nameof(MeasurementStatusCode.Sps30DisabledOnBattery);
        if (string.Equals(measurement.PowerSource, "BAT", StringComparison.OrdinalIgnoreCase)) return nameof(MeasurementStatusCode.BackupPowerActive);
        if (measurement.ReceivedSampleCount < ExpectedSampleCount / 2) return nameof(MeasurementStatusCode.InsufficientSamples);
        return nameof(MeasurementStatusCode.Ok);
    }

    /// <summary>
    /// Stores quality flags; channels and checks are created on first use and a check
    /// switched off in <c>QualityChecks.Enabled</c> is skipped. Returns the number of stored flags.
    /// </summary>
    private async Task<int> AddQualityFlagsAsync(
        Measurement measurement,
        IReadOnlyList<QualityFlagCandidate> flags,
        CancellationToken ct)
    {
        Dictionary<string, SensorChannel> channels = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, QualityCheck> checks = new(StringComparer.OrdinalIgnoreCase);
        int stored = 0;

        foreach (QualityFlagCandidate flag in flags)
        {
            if (!checks.TryGetValue(flag.CheckCode, out QualityCheck? check))
            {
                check = await dbContext.QualityChecks.SingleOrDefaultAsync(item => item.Code == flag.CheckCode, ct);
                if (check is null)
                {
                    check = new QualityCheck
                    {
                        Code = flag.CheckCode,
                        Name = flag.CheckCode,
                        Description = QualityCatalog.DescribeCheck(flag.CheckCode),
                        DefaultSeverity = flag.Severity,
                        Enabled = true
                    };
                    dbContext.QualityChecks.Add(check);
                }

                checks.Add(flag.CheckCode, check);
            }

            if (!check.Enabled)
            {
                continue;
            }

            if (!channels.TryGetValue(flag.ChannelCode, out SensorChannel? channel))
            {
                channel = await dbContext.SensorChannels.SingleOrDefaultAsync(item => item.Code == flag.ChannelCode, ct);
                if (channel is null)
                {
                    (string name, string unit) = QualityCatalog.DescribeChannel(flag.ChannelCode);
                    channel = new SensorChannel { Code = flag.ChannelCode, Name = name, Unit = unit };
                    dbContext.SensorChannels.Add(channel);
                }

                channels.Add(flag.ChannelCode, channel);
            }

            dbContext.MeasurementQualityFlags.Add(new MeasurementQualityFlag
            {
                Measurement = measurement,
                SensorChannel = channel,
                QualityCheck = check,
                Severity = flag.Severity,
                Message = flag.Message,
                CreatedAt = DateTimeOffset.UtcNow
            });
            stored++;
        }

        return stored;
    }

    private static bool IsOk(string value)
        => IsStatus(value, "OK") || IsStatus(value, nameof(SensorErrorCode.None));

    private static bool IsStatus(string value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset AlignIntervalStart(DateTimeOffset value)
    {
        long ticks = value.ToUniversalTime().Ticks;
        long intervalTicks = Interval.Ticks;
        return new DateTimeOffset(ticks - (ticks % intervalTicks), TimeSpan.Zero);
    }

    private static long CreateSequenceNumber(DateTimeOffset timestamp)
        => long.Parse(timestamp.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private sealed class Statistics
    {
        public double? Mean { get; private init; }
        public double? Minimum { get; private init; }
        public double? Maximum { get; private init; }
        public double? StandardDeviation { get; private init; }

        public static Statistics From(IEnumerable<double?> source)
        {
            double[] values = source.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            if (values.Length == 0) return new Statistics();

            // Sample standard deviation (Bessel correction, n - 1); 0 for a single value.
            double mean = values.Average();
            double variance = values.Length < 2
                ? 0
                : values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);

            return new Statistics
            {
                Mean = mean,
                Minimum = values.Min(),
                Maximum = values.Max(),
                StandardDeviation = Math.Sqrt(variance)
            };
        }
    }
}
