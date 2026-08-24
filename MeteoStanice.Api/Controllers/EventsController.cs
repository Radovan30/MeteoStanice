using MeteoStanice.Api.Security;
using MeteoStanice.Contracts.Events;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Controllers;

/// <summary>
/// Accepts device events from meteo stations and exposes recent records.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class EventsController(MeteoDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Stores one device event uploaded by station firmware.
    /// </summary>
    [HttpPost]
    [StationApiKey]
    public async Task<ActionResult<DeviceEventDto>> Upload(DeviceEventUploadRequest request, CancellationToken ct)
    {
        Station station = await GetOrCreateStation(request, ct);
        station.LastSeenAt = DateTimeOffset.UtcNow;
        if (station.Id == 0)
        {
            dbContext.Stations.Add(station);
            await dbContext.SaveChangesAsync(ct);
        }

        DeviceEvent? existingEvent = await dbContext.DeviceEvents
            .SingleOrDefaultAsync(item => item.StationId == station.Id && item.SequenceNumber == request.SequenceNumber, ct);
        if (existingEvent is not null)
        {
            return Ok(MapEvent(existingEvent, station.StationGuid));
        }

        DeviceEvent deviceEvent = new()
        {
            Station = station,
            SequenceNumber = request.SequenceNumber,
            OccurredAt = request.OccurredAt == default ? DateTimeOffset.UtcNow : request.OccurredAt,
            Severity = request.Severity,
            Category = request.Category,
            Code = request.Code,
            Component = request.Component,
            Message = request.Message,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.DeviceEvents.Add(deviceEvent);
        await dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetRecent), new { stationGuid = station.StationGuid }, MapEvent(deviceEvent, station.StationGuid));
    }

    /// <summary>
    /// Returns recent device events for one station.
    /// </summary>
    [HttpGet("{stationGuid:guid}/recent")]
    public async Task<ActionResult<IReadOnlyList<DeviceEventDto>>> GetRecent(Guid stationGuid, int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        List<DeviceEventDto> events = await dbContext.DeviceEvents
            .Where(item => item.Station.StationGuid == stationGuid)
            .OrderByDescending(item => item.OccurredAt)
            .Take(take)
            .Select(item => new DeviceEventDto
            {
                Id = item.Id,
                StationGuid = item.Station.StationGuid,
                SequenceNumber = item.SequenceNumber,
                OccurredAt = item.OccurredAt,
                Severity = item.Severity,
                Category = item.Category,
                Code = item.Code,
                Component = item.Component,
                Message = item.Message
            })
            .ToListAsync(ct);

        return events;
    }

    private async Task<Station> GetOrCreateStation(DeviceEventUploadRequest request, CancellationToken ct)
    {
        string stationCode = GetStationCode(request);
        if (request.StationGuid != Guid.Empty)
        {
            Station? stationByGuid = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == request.StationGuid, ct);
            if (stationByGuid is not null)
            {
                UpdateStationMetadata(stationByGuid, stationCode);
                return stationByGuid;
            }
        }

        Station? stationByCode = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationCode == stationCode, ct);
        if (stationByCode is not null)
        {
            UpdateStationMetadata(stationByCode, stationCode);
            return stationByCode;
        }

        return new Station
        {
            StationGuid = request.StationGuid == Guid.Empty ? Guid.NewGuid() : request.StationGuid,
            StationCode = stationCode,
            Name = stationCode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    private static void UpdateStationMetadata(Station station, string stationCode)
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

    private static string GetStationCode(DeviceEventUploadRequest request)
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

    private static DeviceEventDto MapEvent(DeviceEvent deviceEvent, Guid stationGuid)
        => new()
        {
            Id = deviceEvent.Id,
            StationGuid = stationGuid,
            SequenceNumber = deviceEvent.SequenceNumber,
            OccurredAt = deviceEvent.OccurredAt,
            Severity = deviceEvent.Severity,
            Category = deviceEvent.Category,
            Code = deviceEvent.Code,
            Component = deviceEvent.Component,
            Message = deviceEvent.Message
        };
}
