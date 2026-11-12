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
}
