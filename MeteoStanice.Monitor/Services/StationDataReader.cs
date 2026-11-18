using MeteoStanice.Contracts.Samples;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using MeteoStanice.Monitor.Models;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Cte aktualni stav stanic z PostgreSQL. Pouze cteni – zadne SaveChanges, zadne migrace.
/// </summary>
public sealed class StationDataReader(string connectionString)
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(10);

    private readonly DbContextOptions<MeteoDbContext> _options = new DbContextOptionsBuilder<MeteoDbContext>()
        .UseNpgsql(connectionString)
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
        .Options;

    /// <summary>
    /// Nacte vsechny aktivni stanice vcetne posledniho mereni a recentnich udalosti.
    /// </summary>
    public async Task<IReadOnlyList<StationSnapshot>> ReadAsync(TimeSpan eventLookback, CancellationToken ct)
    {
        await using MeteoDbContext db = new(_options);

        List<Station> stations = await db.Stations
            .Where(station => station.IsActive)
            .OrderBy(station => station.StationCode)
            .ToListAsync(ct);

        Dictionary<Guid, int> intervals = await db.StationSettings
            .ToDictionaryAsync(setting => setting.StationId, setting => setting.MeasurementIntervalMinutes, ct);

        // CreatedAt je cas serveru – spolehlivejsi nez OccurredAt, ktery muze byt chybny pri nesynchronizovanem case stanice.
        DateTimeOffset since = DateTimeOffset.UtcNow - eventLookback;
        List<DeviceEvent> events = await db.DeviceEvents
            .Where(deviceEvent => deviceEvent.CreatedAt >= since)
            .OrderBy(deviceEvent => deviceEvent.CreatedAt)
            .ThenBy(deviceEvent => deviceEvent.SequenceNumber)
            .ToListAsync(ct);
        ILookup<Guid, DeviceEvent> eventsByStation = events.ToLookup(deviceEvent => deviceEvent.StationId);

        // Zaznam s casem v budoucnosti (chybny cas stanice) by jinak zustal "poslednim" a prekryl vsechna dalsi mereni.
        DateTimeOffset futureLimit = DateTimeOffset.UtcNow + FutureTolerance;

        List<StationSnapshot> snapshots = new(stations.Count);
        foreach (Station station in stations)
        {
            Measurement? latest = await db.Measurements
                .Where(measurement => measurement.StationId == station.Id && measurement.MeasuredAt <= futureLimit)
                .OrderByDescending(measurement => measurement.MeasuredAt)
                .ThenByDescending(measurement => measurement.CreatedAt)
                .Include(measurement => measurement.QualityFlags).ThenInclude(flag => flag.SensorChannel)
                .Include(measurement => measurement.QualityFlags).ThenInclude(flag => flag.QualityCheck)
                .AsSplitQuery()
                .FirstOrDefaultAsync(ct);

            int futureCount = await db.Measurements
                .CountAsync(measurement => measurement.StationId == station.Id && measurement.MeasuredAt > futureLimit, ct);

            if (latest is not null)
            {
                MarkMissingValues(latest);
                ApplySps30Correction(latest, station.Sps30CorrectionPercent);
            }

            snapshots.Add(new StationSnapshot
            {
                Station = station,
                LatestMeasurement = latest,
                LatestFlags = latest?.QualityFlags.ToList() ?? [],
                RecentEvents = eventsByStation[station.Id].ToList(),
                MeasurementIntervalMinutes = intervals.GetValueOrDefault(station.Id),
                FutureMeasurementCount = futureCount
            });
        }

        return snapshots;
    }

    /// <summary>
    /// Nacte posledni 10s vzorek kazde aktivni stanice (klic = StationGuid). Pro kazdou stanici
    /// jeden dotaz pres index (StationId, MeasuredAt) – tabulka SensorSamples je velka.
    /// </summary>
    public async Task<Dictionary<Guid, SensorSampleDto>> ReadLatestSamplesAsync(CancellationToken ct)
    {
        await using MeteoDbContext db = new(_options);
        var stations = await db.Stations
            .Where(station => station.IsActive)
            .Select(station => new { station.Id, station.StationGuid, station.StationCode, station.Name })
            .ToListAsync(ct);

        DateTimeOffset futureLimit = DateTimeOffset.UtcNow + FutureTolerance;
        Dictionary<Guid, SensorSampleDto> result = [];
        foreach (var station in stations)
        {
            SensorSample? sample = await db.SensorSamples
                .Where(item => item.StationId == station.Id && item.MeasuredAt <= futureLimit)
                .OrderByDescending(item => item.MeasuredAt)
                .FirstOrDefaultAsync(ct);
            if (sample is null)
            {
                continue;
            }

            result[station.StationGuid] = new SensorSampleDto
            {
                Id = sample.Id,
                StationGuid = station.StationGuid,
                StationCode = station.StationCode,
                StationName = station.Name,
                SequenceNumber = sample.SequenceNumber,
                MeasuredAt = sample.MeasuredAt,
                CreatedAt = sample.CreatedAt,
                TemperatureCelsius = sample.TemperatureCelsius,
                HumidityPercent = sample.HumidityPercent,
                PressureHpa = sample.PressureHpa,
                Bmp280TemperatureCelsius = sample.Bmp280TemperatureCelsius,
                Pm1 = sample.Pm1,
                Pm25 = sample.Pm25,
                Pm4 = sample.Pm4,
                Pm10 = sample.Pm10,
                NumberPm05 = sample.NumberPm05,
                NumberPm1 = sample.NumberPm1,
                NumberPm25 = sample.NumberPm25,
                NumberPm4 = sample.NumberPm4,
                NumberPm10 = sample.NumberPm10,
                TypicalParticleSize = sample.TypicalParticleSize,
                BatteryVoltage = sample.BatteryVoltage,
                BatteryLow = sample.BatteryLow,
                PowerSource = sample.PowerSource,
                Sht40Status = sample.Sht40Status,
                Bmp280Status = sample.Bmp280Status,
                Sps30Status = sample.Sps30Status,
                Status = sample.Status
            };
        }

        return result;
    }

    /// <summary>
    /// Firmware posila pri nulovem poctu platnych vzorku hodnotu 0 misto null (Average: count == 0 ? 0 : ...).
    /// Takova 0 neni namerena hodnota, proto se pro ZOBRAZENI nahradi null (v DB zustava beze zmeny,
    /// entita je nactena bez sledovani zmen a nikdy se neuklada).
    /// </summary>
    private static void MarkMissingValues(Measurement measurement)
    {
        if (measurement.Sps30OkCount == 0)
        {
            measurement.Pm1 = measurement.Pm25 = measurement.Pm4 = measurement.Pm10 = null;
            measurement.NumberPm05 = measurement.NumberPm1 = measurement.NumberPm25 = measurement.NumberPm4 = measurement.NumberPm10 = null;
            measurement.TypicalParticleSize = null;
            measurement.Pm25Min = measurement.Pm25Max = measurement.Pm25StdDev = null;
            measurement.Pm10Min = measurement.Pm10Max = measurement.Pm10StdDev = null;
        }

        if (measurement.Sht40OkCount == 0)
        {
            measurement.TemperatureCelsius = measurement.HumidityPercent = null;
            measurement.TemperatureMin = measurement.TemperatureMax = measurement.TemperatureStdDev = null;
            measurement.HumidityMin = measurement.HumidityMax = measurement.HumidityStdDev = null;
        }

        if (measurement.Bmp280OkCount == 0)
        {
            measurement.PressureHpa = null;
            measurement.PressureMin = measurement.PressureMax = measurement.PressureStdDev = null;
        }
    }

    /// <summary>
    /// Applies the station-specific SPS30 display correction to an untracked entity.
    /// Raw database values remain unchanged.
    /// </summary>
    private static void ApplySps30Correction(Measurement measurement, double correctionPercent)
    {
        if (correctionPercent == 0 || measurement.Sps30OkCount == 0)
        {
            return;
        }

        double factor = 1 + correctionPercent / 100.0;
        measurement.Pm1 *= factor;
        measurement.Pm25 *= factor;
        measurement.Pm4 *= factor;
        measurement.Pm10 *= factor;
        measurement.Pm25Min *= factor;
        measurement.Pm25Max *= factor;
        measurement.Pm25StdDev *= factor;
        measurement.Pm10Min *= factor;
        measurement.Pm10Max *= factor;
        measurement.Pm10StdDev *= factor;
    }
}
