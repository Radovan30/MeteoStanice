using System.Globalization;
using System.Text;
using MeteoStanice.Api.Security;
using MeteoStanice.Api.Services;
using MeteoStanice.Contracts.Samples;
using MeteoStanice.Contracts.Protocol;
using MeteoStanice.Contracts.Statuses;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Controllers;

/// <summary>
/// Stores and exposes raw sensor samples used for analysis and backend aggregation.
/// </summary>
[ApiController]
[Route("api/samples")]
public sealed class SamplesController(
    MeteoDbContext dbContext,
    StationRegistrationService registrationService,
    SensorSampleAggregationService aggregationService) : ControllerBase
{
    private const int MaximumBatchSize = 500;
    private const int MaximumPageSize = 1000;

    /// <summary>
    /// Stores one idempotent batch of raw samples and registers the station on first contact.
    /// Samples with an implausible timestamp (clock not synchronized) are acknowledged but not stored,
    /// so the station removes them instead of retrying forever.
    /// </summary>
    [HttpPost]
    [StationApiKey]
    public async Task<ActionResult<SensorSampleUploadResultDto>> Upload(
        SensorSampleUploadRequest request,
        CancellationToken ct)
    {
        if (!FirmwareProtocol.IsSupported(request.ProtocolVersion))
        {
            return BadRequest(new
            {
                message = "Unsupported protocol version.",
                received = request.ProtocolVersion,
                supported = FirmwareProtocol.CurrentVersion
            });
        }

        if (request.StationGuid == Guid.Empty)
        {
            return BadRequest(new { message = "StationGuid is required." });
        }

        if (request.Samples.Count == 0 || request.Samples.Count > MaximumBatchSize)
        {
            return BadRequest(new { message = $"Samples must contain between 1 and {MaximumBatchSize} items." });
        }

        Station station = await registrationService.GetOrCreateAsync(
            new StationUploadIdentity(
                request.StationGuid,
                request.StationCode,
                request.HardwareId,
                request.FirmwareVersion,
                request.ProtocolVersion,
                request.LocationText),
            ct);

        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        HashSet<long> batchSequences = new();
        List<(SensorSampleUploadItem Item, long SequenceNumber)> validItems = new(request.Samples.Count);
        int duplicateCount = 0;
        int rejectedCount = 0;

        foreach (SensorSampleUploadItem item in request.Samples)
        {
            if (!UploadTimeValidator.IsPlausible(item.MeasuredAt, createdAt))
            {
                rejectedCount++;
                continue;
            }

            long sequenceNumber = item.SequenceNumber > 0
                ? item.SequenceNumber
                : CreateSequenceNumber(item.MeasuredAt);

            if (!batchSequences.Add(sequenceNumber))
            {
                duplicateCount++;
                continue;
            }

            validItems.Add((item, sequenceNumber));
        }

        long[] requestedSequences = validItems.Select(item => item.SequenceNumber).ToArray();
        HashSet<long> existingSequences = await dbContext.SensorSamples
            .Where(item => item.StationId == station.Id && requestedSequences.Contains(item.SequenceNumber))
            .Select(item => item.SequenceNumber)
            .ToHashSetAsync(ct);

        List<DateTimeOffset> storedTimes = new(validItems.Count);
        foreach ((SensorSampleUploadItem item, long sequenceNumber) in validItems)
        {
            if (existingSequences.Contains(sequenceNumber))
            {
                duplicateCount++;
                continue;
            }

            dbContext.SensorSamples.Add(MapEntity(item, sequenceNumber, station, createdAt));
            storedTimes.Add(item.MeasuredAt);
        }

        if (storedTimes.Count > 0)
        {
            // LastSeenAt = time of the last newly stored record, not of a repeated (duplicate) upload.
            station.LastSeenAt = createdAt;
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (storedTimes.Count > 0)
        {
            // A concurrent request stored the same samples first; the station simply retries the batch.
            return Conflict(new { message = "Samples were stored concurrently, retry the batch." });
        }

        if (storedTimes.Count > 0)
        {
            await aggregationService.RebuildAffectedIntervalsAsync(station, storedTimes, ct);
        }

        return Ok(new SensorSampleUploadResultDto
        {
            StationGuid = station.StationGuid,
            StationCode = station.StationCode,
            ReceivedCount = request.Samples.Count,
            StoredCount = storedTimes.Count,
            DuplicateCount = duplicateCount,
            RejectedCount = rejectedCount
        });
    }

    /// <summary>
    /// Returns a filtered page of raw samples, newest first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SensorSamplePageDto>> GetPage(
        Guid? stationGuid = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 200,
        CancellationToken ct = default)
    {
        if (!ValidateTimeRange(from, to, out string? error))
        {
            return BadRequest(new { message = error });
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);

        IQueryable<SensorSample> query = ApplyFilters(dbContext.SensorSamples.AsNoTracking(), stationGuid, from, to);
        long totalCount = await query.LongCountAsync(ct);

        List<SensorSampleDto> items = await Project(query)
            .OrderByDescending(item => item.MeasuredAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        foreach (SensorSampleDto item in items)
        {
            NormalizeStatuses(item);
        }

        return new SensorSamplePageDto
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items
        };
    }

    /// <summary>
    /// Returns the latest raw sample for every active station or for one selected station.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<IReadOnlyList<SensorSampleDto>>> GetLatest(
        Guid? stationGuid = null,
        CancellationToken ct = default)
    {
        IQueryable<SensorSample> samples = dbContext.SensorSamples.AsNoTracking();
        if (stationGuid.HasValue)
        {
            samples = samples.Where(item => item.Station.StationGuid == stationGuid.Value);
        }

        List<Guid> stationIds = await samples
            .Select(item => item.StationId)
            .Distinct()
            .ToListAsync(ct);

        List<SensorSampleDto> result = new(stationIds.Count);
        foreach (Guid stationId in stationIds)
        {
            SensorSampleDto? latest = await Project(samples.Where(item => item.StationId == stationId))
                .OrderByDescending(item => item.MeasuredAt)
                .ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(ct);
            if (latest is not null)
            {
                NormalizeStatuses(latest);
                result.Add(latest);
            }
        }

        return result.OrderBy(item => item.StationCode).ToList();
    }

    /// <summary>
    /// Streams raw samples as invariant-culture CSV for statistical analysis.
    /// </summary>
    [HttpGet("export.csv")]
    public async Task ExportCsv(
        Guid? stationGuid = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        if (!ValidateTimeRange(from, to, out string? error))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = error }, ct);
            return;
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=meteostanice-samples-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

        IQueryable<SensorSampleDto> query = Project(ApplyFilters(
                dbContext.SensorSamples.AsNoTracking(), stationGuid, from, to))
            .OrderBy(item => item.MeasuredAt)
            .ThenBy(item => item.StationCode);

        await using StreamWriter writer = new(Response.Body, new UTF8Encoding(false), 16 * 1024, leaveOpen: true);
        await writer.WriteLineAsync(
            "Id,StationGuid,StationCode,SequenceNumber,MeasuredAtUtc,ReceivedAtUtc,TemperatureCelsius,HumidityPercent,PressureHpa,Bmp280TemperatureCelsius,Pm1,Pm25,Pm4,Pm10,NumberPm05,NumberPm1,NumberPm25,NumberPm4,NumberPm10,TypicalParticleSize,BatteryVoltage,BatteryLow,PowerSource,Sht40Status,Bmp280Status,Sps30Status,Status");

        await foreach (SensorSampleDto sample in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(ToCsvRow(sample));
        }

        await writer.FlushAsync(ct);
    }

    private static IQueryable<SensorSample> ApplyFilters(
        IQueryable<SensorSample> query,
        Guid? stationGuid,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (stationGuid.HasValue)
        {
            query = query.Where(item => item.Station.StationGuid == stationGuid.Value);
        }

        if (from.HasValue)
        {
            DateTimeOffset fromUtc = from.Value.ToUniversalTime();
            query = query.Where(item => item.MeasuredAt >= fromUtc);
        }

        if (to.HasValue)
        {
            DateTimeOffset toUtc = to.Value.ToUniversalTime();
            query = query.Where(item => item.MeasuredAt < toUtc);
        }

        return query;
    }

    private static IQueryable<SensorSampleDto> Project(IQueryable<SensorSample> query)
        => query.Select(item => new SensorSampleDto
        {
            Id = item.Id,
            StationGuid = item.Station.StationGuid,
            StationCode = item.Station.StationCode,
            StationName = item.Station.Name,
            SequenceNumber = item.SequenceNumber,
            MeasuredAt = item.MeasuredAt,
            CreatedAt = item.CreatedAt,
            TemperatureCelsius = item.TemperatureCelsius,
            HumidityPercent = item.HumidityPercent,
            PressureHpa = item.PressureHpa,
            Bmp280TemperatureCelsius = item.Bmp280TemperatureCelsius,
            Pm1 = item.Pm1,
            Pm25 = item.Pm25,
            Pm4 = item.Pm4,
            Pm10 = item.Pm10,
            NumberPm05 = item.NumberPm05,
            NumberPm1 = item.NumberPm1,
            NumberPm25 = item.NumberPm25,
            NumberPm4 = item.NumberPm4,
            NumberPm10 = item.NumberPm10,
            TypicalParticleSize = item.TypicalParticleSize,
            BatteryVoltage = item.BatteryVoltage,
            BatteryLow = item.BatteryLow,
            PowerSource = item.PowerSource,
            Sht40Status = item.Sht40Status,
            Bmp280Status = item.Bmp280Status,
            Sps30Status = item.Sps30Status,
            Status = item.Status
        });

    private static SensorSample MapEntity(
        SensorSampleUploadItem item,
        long sequenceNumber,
        Station station,
        DateTimeOffset createdAt)
        => new()
        {
            Station = station,
            SequenceNumber = sequenceNumber,
            MeasuredAt = item.MeasuredAt.ToUniversalTime(),
            TemperatureCelsius = item.TemperatureCelsius,
            HumidityPercent = item.HumidityPercent,
            PressureHpa = item.PressureHpa,
            Bmp280TemperatureCelsius = item.Bmp280TemperatureCelsius,
            Pm1 = item.Pm1,
            Pm25 = item.Pm25,
            Pm4 = item.Pm4,
            Pm10 = item.Pm10,
            NumberPm05 = item.NumberPm05,
            NumberPm1 = item.NumberPm1,
            NumberPm25 = item.NumberPm25,
            NumberPm4 = item.NumberPm4,
            NumberPm10 = item.NumberPm10,
            TypicalParticleSize = item.TypicalParticleSize,
            BatteryVoltage = item.BatteryVoltage,
            BatteryLow = item.BatteryLow,
            PowerSource = FirmwareStatusCatalog.NormalizePowerSource(item.PowerSource),
            Sht40Status = FirmwareStatusCatalog.NormalizeSensorError(item.Sht40Status),
            Bmp280Status = FirmwareStatusCatalog.NormalizeSensorError(item.Bmp280Status),
            Sps30Status = FirmwareStatusCatalog.NormalizeSensorError(item.Sps30Status),
            Status = FirmwareStatusCatalog.NormalizeRawSampleStatus(item.Status),
            CreatedAt = createdAt
        };

    private static bool ValidateTimeRange(DateTimeOffset? from, DateTimeOffset? to, out string? error)
    {
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            error = "The 'from' timestamp must be earlier than 'to'.";
            return false;
        }

        error = null;
        return true;
    }

    private static long CreateSequenceNumber(DateTimeOffset timestamp)
        => long.Parse(timestamp.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static void NormalizeStatuses(SensorSampleDto sample)
    {
        sample.PowerSource = FirmwareStatusCatalog.NormalizePowerSource(sample.PowerSource);
        sample.Sht40Status = FirmwareStatusCatalog.NormalizeSensorError(sample.Sht40Status);
        sample.Bmp280Status = FirmwareStatusCatalog.NormalizeSensorError(sample.Bmp280Status);
        sample.Sps30Status = FirmwareStatusCatalog.NormalizeSensorError(sample.Sps30Status);
        sample.Status = FirmwareStatusCatalog.NormalizeRawSampleStatus(sample.Status);
    }

    private static string ToCsvRow(SensorSampleDto sample)
        => string.Join(",",
            Csv(sample.Id.ToString()),
            Csv(sample.StationGuid.ToString()),
            Csv(sample.StationCode),
            sample.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            Csv(sample.MeasuredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            Csv(sample.CreatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            Number(sample.TemperatureCelsius),
            Number(sample.HumidityPercent),
            Number(sample.PressureHpa),
            Number(sample.Bmp280TemperatureCelsius),
            Number(sample.Pm1),
            Number(sample.Pm25),
            Number(sample.Pm4),
            Number(sample.Pm10),
            Number(sample.NumberPm05),
            Number(sample.NumberPm1),
            Number(sample.NumberPm25),
            Number(sample.NumberPm4),
            Number(sample.NumberPm10),
            Number(sample.TypicalParticleSize),
            Number(sample.BatteryVoltage),
            sample.BatteryLow ? "true" : "false",
            Csv(FirmwareStatusCatalog.NormalizePowerSource(sample.PowerSource)),
            Csv(FirmwareStatusCatalog.NormalizeSensorError(sample.Sht40Status)),
            Csv(FirmwareStatusCatalog.NormalizeSensorError(sample.Bmp280Status)),
            Csv(FirmwareStatusCatalog.NormalizeSensorError(sample.Sps30Status)),
            Csv(FirmwareStatusCatalog.NormalizeRawSampleStatus(sample.Status)));

    private static string Number(double? value)
        => value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Csv(string? value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
