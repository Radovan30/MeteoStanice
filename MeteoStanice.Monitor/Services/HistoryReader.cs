using Npgsql;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Cte prubeh 10s vzorku vybranych stanic za zvolene obdobi z tabulky SensorSamples (pouze cteni).
/// Vzorky se prumeruji do casovych oken tak, aby krivka mela nejvyse priblizne 360 bodu.
/// </summary>
public sealed class HistoryReader(string connectionString)
{
    private const string Sql = """
        SELECT s."StationId",
               to_timestamp(floor(extract(epoch FROM s."MeasuredAt")::double precision / @bucket) * @bucket) AS t,
               avg(s."Pm1"), avg(s."Pm25"), avg(s."Pm4"), avg(s."Pm10"),
               avg(s."TemperatureCelsius"), avg(s."HumidityPercent"), avg(s."PressureHpa")
        FROM "SensorSamples" s
        WHERE s."StationId" = ANY(@stations) AND s."MeasuredAt" >= @from AND s."MeasuredAt" <= @to
        GROUP BY 1, 2
        ORDER BY 1, 2
        """;

    private const string StationSql = """
        SELECT "Id", "StationCode", "LocationText", "Sps30CorrectionPercent" FROM "Stations" WHERE "Id" = ANY(@stations)
        """;

    /// <summary>
    /// Delka prumerovaciho okna pro dane obdobi: 1 h = 10 s (surove vzorky), 2 h = 20 s, 8 h = 80 s, 24 h = 4 min.
    /// </summary>
    public static int BucketSecondsFor(TimeSpan range)
        => Math.Max(10, (int)Math.Ceiling(range.TotalSeconds / 360.0 / 10.0) * 10);

    public async Task<HistoryResult> ReadAsync(IReadOnlyCollection<Guid> stationIds, TimeSpan range, PressureReductionOptions reduction, CancellationToken ct)
    {
        DateTimeOffset to = DateTimeOffset.UtcNow;
        DateTimeOffset from = to - range;
        int bucket = BucketSecondsFor(range);
        Dictionary<Guid, List<HistoryPoint>> series = stationIds.ToDictionary(id => id, _ => new List<HistoryPoint>());
        if (stationIds.Count == 0)
        {
            return new HistoryResult(from, to, bucket, series);
        }

        Guid[] ids = stationIds.ToArray();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(ct);

        Dictionary<Guid, (double Altitude, double Factor)> stationInfo = [];
        await using (NpgsqlCommand stationCommand = new(StationSql, connection))
        {
            stationCommand.Parameters.AddWithValue("stations", ids);
            await using NpgsqlDataReader stationReader = await stationCommand.ExecuteReaderAsync(ct);
            while (await stationReader.ReadAsync(ct))
            {
                string code = stationReader.GetString(1);
                string location = stationReader.IsDBNull(2) ? string.Empty : stationReader.GetString(2);
                double correction = stationReader.GetDouble(3);
                stationInfo[stationReader.GetGuid(0)] = (reduction.AltitudeFor(code, location), 1 + correction / 100.0);
            }
        }

        await using (NpgsqlCommand command = new(Sql, connection))
        {
            command.Parameters.AddWithValue("bucket", (double)bucket);
            command.Parameters.AddWithValue("stations", ids);
            command.Parameters.AddWithValue("from", from);
            command.Parameters.AddWithValue("to", to);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                Guid stationId = reader.GetGuid(0);
                (double altitude, double factor) = stationInfo.GetValueOrDefault(stationId, (reduction.DefaultAltitudeMeters, 1.0));
                double? temperature = Read(reader, 6);
                series[stationId].Add(new HistoryPoint(
                    reader.GetFieldValue<DateTimeOffset>(1),
                    Read(reader, 2) * factor,
                    Read(reader, 3) * factor,
                    Read(reader, 4) * factor,
                    Read(reader, 5) * factor,
                    temperature,
                    Read(reader, 7),
                    PressureReduction.ToSeaLevel(Read(reader, 8), temperature, altitude, reduction.UseMeasuredTemperature)));
            }
        }

        return new HistoryResult(from, to, bucket, series);
    }

    private static double? Read(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}

/// <summary>
/// Jeden bod prubehu (prumer vzorku v casovem okne). PM jsou vcetne korekce SPS30 stanice,
/// tlak je prepocten na hladinu more stejne jako na ukazateli.
/// </summary>
public sealed record HistoryPoint(
    DateTimeOffset Time,
    double? Pm1,
    double? Pm25,
    double? Pm4,
    double? Pm10,
    double? Temperature,
    double? Humidity,
    double? PressureSeaLevel);

/// <summary>
/// Prubehy vybranych stanic za obdobi (klic = Stations.Id).
/// </summary>
public sealed record HistoryResult(DateTimeOffset From, DateTimeOffset To, int BucketSeconds, IReadOnlyDictionary<Guid, List<HistoryPoint>> Series);
