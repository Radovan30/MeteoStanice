using MeteoStanice.Api.Hubs;
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
    public async Task<ActionResult<MeasurementDto>> Upload(MeasurementUploadRequest request, CancellationToken ct)
    {
        Station station = await dbContext.Stations
            .SingleOrDefaultAsync(item => item.StationGuid == request.StationGuid, ct)
            ?? CreateStation(request);

        station.LastSeenAt = DateTimeOffset.UtcNow;
        if (station.Id == 0)
        {
            dbContext.Stations.Add(station);
        }

        Measurement measurement = new()
        {
            Station = station,
            SequenceNumber = request.SequenceNumber,
            MeasuredAt = request.MeasuredAt,
            TemperatureCelsius = request.TemperatureCelsius,
            HumidityPercent = request.HumidityPercent,
            PressureHpa = request.PressureHpa,
            Pm25 = request.Pm25,
            Pm10 = request.Pm10,
            BatteryVoltage = request.BatteryVoltage,
            PowerSource = request.PowerSource,
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
                Pm25 = item.Pm25,
                Pm10 = item.Pm10,
                PowerSource = item.PowerSource,
                QualityFlagCount = item.QualityFlags.Count
            })
            .ToListAsync(ct);

        return measurements;
    }

    private static Station CreateStation(MeasurementUploadRequest request)
        => new()
        {
            StationGuid = request.StationGuid,
            StationCode = string.IsNullOrWhiteSpace(request.StationCode) ? request.StationGuid.ToString() : request.StationCode,
            Name = string.IsNullOrWhiteSpace(request.StationCode) ? "Meteo stanice" : request.StationCode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

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
            Pm25 = measurement.Pm25,
            Pm10 = measurement.Pm10,
            PowerSource = measurement.PowerSource,
            QualityFlagCount = qualityFlagCount
        };
}
