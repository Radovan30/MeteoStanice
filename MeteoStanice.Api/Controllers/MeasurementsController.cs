using MeteoStanice.Api.Hubs;
using MeteoStanice.Api.Security;
using MeteoStanice.Contracts.Measurements;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using MeteoStanice.Quality;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Controllers;

/// <summary>
/// Accepts measurements from meteo stations and exposes recent records.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class MeasurementsController(
    MeteoDbContext dbContext,
    MeasurementQualityService qualityService,
    IHubContext<MeteoHub> meteoHubContext) : ControllerBase
{
    /// <summary>
    /// Stores one measurement uploaded by station firmware.
    /// </summary>
    [HttpPost]
    [StationApiKey]
    public async Task<ActionResult<MeasurementDto>> Upload(MeasurementUploadRequest request, CancellationToken ct)
    {
        Station station = await GetOrCreateStation(request, ct);

        station.LastSeenAt = DateTimeOffset.UtcNow;
        if (station.Id == 0)
        {
            dbContext.Stations.Add(station);
            await dbContext.SaveChangesAsync(ct);
        }

        Measurement? existingMeasurement = await dbContext.Measurements
            .Include(item => item.QualityFlags)
            .SingleOrDefaultAsync(item => item.StationId == station.Id && item.SequenceNumber == request.SequenceNumber, ct);
        if (existingMeasurement is not null)
        {
            return Ok(MapMeasurement(existingMeasurement, station.StationGuid, existingMeasurement.QualityFlags.Count));
        }

        Measurement measurement = new()
        {
            Station = station,
            SequenceNumber = request.SequenceNumber,
            MeasuredAt = GetMeasuredAt(request),
            TemperatureCelsius = request.TemperatureCelsius,
            HumidityPercent = request.HumidityPercent,
            PressureHpa = request.PressureHpa,
            Pm1 = request.Pm1,
            Pm25 = request.Pm25,
            Pm4 = request.Pm4,
            Pm10 = request.Pm10,
            NumberPm05 = request.NumberPm05,
            NumberPm1 = request.NumberPm1,
            NumberPm25 = request.NumberPm25,
            NumberPm4 = request.NumberPm4,
            NumberPm10 = request.NumberPm10,
            TypicalParticleSize = request.TypicalParticleSize,
            BatteryVoltage = request.BatteryVoltage,
            PowerSource = request.PowerSource,
            ExpectedSampleCount = request.ExpectedSampleCount,
            ReceivedSampleCount = request.ReceivedSampleCount,
            Sht40OkCount = request.Sht40OkCount,
            Sht40ErrorCount = request.Sht40ErrorCount,
            Bmp280OkCount = request.Bmp280OkCount,
            Bmp280ErrorCount = request.Bmp280ErrorCount,
            Sps30OkCount = request.Sps30OkCount,
            Sps30WaitCount = request.Sps30WaitCount,
            Sps30ErrorCount = request.Sps30ErrorCount,
            Sps30BatterySkippedCount = request.Sps30BatterySkippedCount,
            TemperatureMin = request.TemperatureMin,
            TemperatureMax = request.TemperatureMax,
            TemperatureStdDev = request.TemperatureStdDev,
            HumidityMin = request.HumidityMin,
            HumidityMax = request.HumidityMax,
            HumidityStdDev = request.HumidityStdDev,
            PressureMin = request.PressureMin,
            PressureMax = request.PressureMax,
            PressureStdDev = request.PressureStdDev,
            Pm25Min = request.Pm25Min,
            Pm25Max = request.Pm25Max,
            Pm25StdDev = request.Pm25StdDev,
            Pm10Min = request.Pm10Min,
            Pm10Max = request.Pm10Max,
            Pm10StdDev = request.Pm10StdDev,
            Status = request.Status,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Measurement? previousMeasurement = await dbContext.Measurements
            .Where(item => item.StationId == station.Id)
            .OrderByDescending(item => item.MeasuredAt)
            .FirstOrDefaultAsync(ct);

        dbContext.Measurements.Add(measurement);
        await dbContext.SaveChangesAsync(ct);

        IReadOnlyList<QualityFlagCandidate> flags = qualityService.Evaluate(measurement, previousMeasurement);
        await StoreQualityFlags(measurement, flags, ct);
        await dbContext.SaveChangesAsync(ct);

        MeasurementDto dto = MapMeasurement(measurement, station.StationGuid, flags.Count);
        await meteoHubContext.Clients.All.SendAsync(MeteoHub.MeasurementReceivedMethod, dto, ct);

        return CreatedAtAction(nameof(GetRecent), new { stationGuid = station.StationGuid }, dto);
    }

    /// <summary>
    /// Returns recent measurements for one station.
    /// </summary>
    [HttpGet("{stationGuid:guid}/recent")]
    public async Task<ActionResult<IReadOnlyList<MeasurementDto>>> GetRecent(Guid stationGuid, int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        List<MeasurementDto> measurements = await dbContext.Measurements
            .Where(item => item.Station.StationGuid == stationGuid)
            .OrderByDescending(item => item.MeasuredAt)
            .Take(take)
            .Select(item => new MeasurementDto
            {
                Id = item.Id,
                StationGuid = item.Station.StationGuid,
                SequenceNumber = item.SequenceNumber,
                MeasuredAt = item.MeasuredAt,
                TemperatureCelsius = item.TemperatureCelsius,
                HumidityPercent = item.HumidityPercent,
                PressureHpa = item.PressureHpa,
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
                PowerSource = item.PowerSource,
                ExpectedSampleCount = item.ExpectedSampleCount,
                ReceivedSampleCount = item.ReceivedSampleCount,
                Sht40OkCount = item.Sht40OkCount,
                Sht40ErrorCount = item.Sht40ErrorCount,
                Bmp280OkCount = item.Bmp280OkCount,
                Bmp280ErrorCount = item.Bmp280ErrorCount,
                Sps30OkCount = item.Sps30OkCount,
                Sps30WaitCount = item.Sps30WaitCount,
                Sps30ErrorCount = item.Sps30ErrorCount,
                Sps30BatterySkippedCount = item.Sps30BatterySkippedCount,
                TemperatureMin = item.TemperatureMin,
                TemperatureMax = item.TemperatureMax,
                TemperatureStdDev = item.TemperatureStdDev,
                HumidityMin = item.HumidityMin,
                HumidityMax = item.HumidityMax,
                HumidityStdDev = item.HumidityStdDev,
                PressureMin = item.PressureMin,
                PressureMax = item.PressureMax,
                PressureStdDev = item.PressureStdDev,
                Pm25Min = item.Pm25Min,
                Pm25Max = item.Pm25Max,
                Pm25StdDev = item.Pm25StdDev,
                Pm10Min = item.Pm10Min,
                Pm10Max = item.Pm10Max,
                Pm10StdDev = item.Pm10StdDev,
                QualityFlagCount = item.QualityFlags.Count
            })
            .ToListAsync(ct);

        return measurements;
    }

    private async Task<Station> GetOrCreateStation(MeasurementUploadRequest request, CancellationToken ct)
    {
        string stationCode = GetStationCode(request);
        if (request.StationGuid != Guid.Empty)
        {
            Station? stationByGuid = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == request.StationGuid, ct);
            if (stationByGuid is not null)
            {
                UpdateStationMetadata(stationByGuid, request, stationCode);
                return stationByGuid;
            }
        }

        Station? stationByCode = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationCode == stationCode, ct);
        if (stationByCode is not null)
        {
            UpdateStationMetadata(stationByCode, request, stationCode);
            return stationByCode;
        }

        return CreateStation(request, stationCode);
    }

    private static Station CreateStation(MeasurementUploadRequest request, string stationCode)
        => new()
        {
            StationGuid = request.StationGuid == Guid.Empty ? Guid.NewGuid() : request.StationGuid,
            StationCode = stationCode,
            Name = stationCode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

    private static void UpdateStationMetadata(Station station, MeasurementUploadRequest request, string stationCode)
    {
        if (string.IsNullOrWhiteSpace(station.StationCode))
        {
            station.StationCode = stationCode;
        }

        if (string.IsNullOrWhiteSpace(station.Name))
        {
            station.Name = stationCode;
        }
    }

    private static string GetStationCode(MeasurementUploadRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.StationCode))
        {
            return request.StationCode;
        }

        if (!string.IsNullOrWhiteSpace(request.StationId))
        {
            return request.StationId;
        }

        return request.StationGuid == Guid.Empty ? "METEO-UNKNOWN" : request.StationGuid.ToString();
    }

    private static DateTimeOffset GetMeasuredAt(MeasurementUploadRequest request)
    {
        if (request.MeasuredAt != default)
        {
            return request.MeasuredAt;
        }

        if (request.SampledAt != default)
        {
            return request.SampledAt;
        }

        return DateTimeOffset.UtcNow;
    }

    private async Task StoreQualityFlags(Measurement measurement, IReadOnlyList<QualityFlagCandidate> flags, CancellationToken ct)
    {
        foreach (QualityFlagCandidate flag in flags)
        {
            SensorChannel channel = await GetOrCreateSensorChannel(flag.ChannelCode, ct);
            QualityCheck check = await GetOrCreateQualityCheck(flag.CheckCode, flag.Severity, ct);

            dbContext.MeasurementQualityFlags.Add(new MeasurementQualityFlag
            {
                Measurement = measurement,
                SensorChannel = channel,
                QualityCheck = check,
                Severity = flag.Severity,
                Message = flag.Message,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task<SensorChannel> GetOrCreateSensorChannel(string code, CancellationToken ct)
    {
        SensorChannel? channel = await dbContext.SensorChannels.SingleOrDefaultAsync(item => item.Code == code, ct);
        if (channel is not null)
        {
            return channel;
        }

        channel = new SensorChannel { Code = code, Name = code, Unit = string.Empty };
        dbContext.SensorChannels.Add(channel);
        return channel;
    }

    private async Task<QualityCheck> GetOrCreateQualityCheck(string code, string severity, CancellationToken ct)
    {
        QualityCheck? check = await dbContext.QualityChecks.SingleOrDefaultAsync(item => item.Code == code, ct);
        if (check is not null)
        {
            return check;
        }

        check = new QualityCheck { Code = code, Name = code, DefaultSeverity = severity, Enabled = true };
        dbContext.QualityChecks.Add(check);
        return check;
    }

    private static MeasurementDto MapMeasurement(Measurement measurement, Guid stationGuid, int qualityFlagCount)
        => new()
        {
            Id = measurement.Id,
            StationGuid = stationGuid,
            SequenceNumber = measurement.SequenceNumber,
            MeasuredAt = measurement.MeasuredAt,
            TemperatureCelsius = measurement.TemperatureCelsius,
            HumidityPercent = measurement.HumidityPercent,
            PressureHpa = measurement.PressureHpa,
            Pm1 = measurement.Pm1,
            Pm25 = measurement.Pm25,
            Pm4 = measurement.Pm4,
            Pm10 = measurement.Pm10,
            NumberPm05 = measurement.NumberPm05,
            NumberPm1 = measurement.NumberPm1,
            NumberPm25 = measurement.NumberPm25,
            NumberPm4 = measurement.NumberPm4,
            NumberPm10 = measurement.NumberPm10,
            TypicalParticleSize = measurement.TypicalParticleSize,
            PowerSource = measurement.PowerSource,
            ExpectedSampleCount = measurement.ExpectedSampleCount,
            ReceivedSampleCount = measurement.ReceivedSampleCount,
            Sht40OkCount = measurement.Sht40OkCount,
            Sht40ErrorCount = measurement.Sht40ErrorCount,
            Bmp280OkCount = measurement.Bmp280OkCount,
            Bmp280ErrorCount = measurement.Bmp280ErrorCount,
            Sps30OkCount = measurement.Sps30OkCount,
            Sps30WaitCount = measurement.Sps30WaitCount,
            Sps30ErrorCount = measurement.Sps30ErrorCount,
            Sps30BatterySkippedCount = measurement.Sps30BatterySkippedCount,
            TemperatureMin = measurement.TemperatureMin,
            TemperatureMax = measurement.TemperatureMax,
            TemperatureStdDev = measurement.TemperatureStdDev,
            HumidityMin = measurement.HumidityMin,
            HumidityMax = measurement.HumidityMax,
            HumidityStdDev = measurement.HumidityStdDev,
            PressureMin = measurement.PressureMin,
            PressureMax = measurement.PressureMax,
            PressureStdDev = measurement.PressureStdDev,
            Pm25Min = measurement.Pm25Min,
            Pm25Max = measurement.Pm25Max,
            Pm25StdDev = measurement.Pm25StdDev,
            Pm10Min = measurement.Pm10Min,
            Pm10Max = measurement.Pm10Max,
            Pm10StdDev = measurement.Pm10StdDev,
            QualityFlagCount = qualityFlagCount
        };
}
