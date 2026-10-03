using MeteoStanice.Api.Security;
using MeteoStanice.Api.Services;
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
        if (dbContext.Entry(station).State == EntityState.Detached)
        {
            station = await SaveNewStationWithRetry(station, ct);
        }

        DeviceEvent? existingEvent = await dbContext.DeviceEvents
            .SingleOrDefaultAsync(item => item.StationId == station.Id && item.SequenceNumber == request.SequenceNumber, ct);
        if (existingEvent is not null)
        {
            return Ok(MapEvent(existingEvent, station));
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

        return CreatedAtAction(nameof(GetRecent), new { stationGuid = station.StationGuid }, MapEvent(deviceEvent, station));
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
                StationCode = item.Station.StationCode,
                StationName = item.Station.Name,
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
        string requestedStationCode = GetStationCode(request);
        string hardwareId = Normalize(request.HardwareId);
        bool generateStationCode = StationCodeAllocator.ShouldGenerate(requestedStationCode);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            Station? stationByHardware = await dbContext.Stations.SingleOrDefaultAsync(item => item.HardwareId == hardwareId, ct);
            if (stationByHardware is not null)
            {
                await UpdateStationMetadata(stationByHardware, requestedStationCode, generateStationCode, hardwareId, request.FirmwareVersion, ct);
                return stationByHardware;
            }
        }

        if (request.StationGuid != Guid.Empty)
        {
            Station? stationByGuid = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == request.StationGuid, ct);
            if (stationByGuid is not null)
            {
                if (IsDifferentHardware(stationByGuid, hardwareId))
                {
                    string collisionStationCode = generateStationCode
                        ? await StationCodeAllocator.GetNextCodeAsync(dbContext, ct)
                        : requestedStationCode;

                    return CreateStation(request, collisionStationCode, hardwareId, Guid.NewGuid());
                }

                await UpdateStationMetadata(stationByGuid, requestedStationCode, generateStationCode, hardwareId, request.FirmwareVersion, ct);
                return stationByGuid;
            }
        }

        if (!generateStationCode)
        {
            Station? stationByCode = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationCode == requestedStationCode, ct);
            if (stationByCode is not null)
            {
                if (IsDifferentHardware(stationByCode, hardwareId))
                {
                    string collisionStationCode = await StationCodeAllocator.GetNextCodeAsync(dbContext, ct);
                    return CreateStation(request, collisionStationCode, hardwareId, Guid.NewGuid());
                }

                await UpdateStationMetadata(stationByCode, requestedStationCode, generateStationCode, hardwareId, request.FirmwareVersion, ct);
                return stationByCode;
            }
        }

        string stationCode = generateStationCode
            ? await StationCodeAllocator.GetNextCodeAsync(dbContext, ct)
            : requestedStationCode;

        return CreateStation(request, stationCode, hardwareId, request.StationGuid);
    }

    private async Task<Station> SaveNewStationWithRetry(Station station, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                dbContext.Stations.Add(station);
                await dbContext.SaveChangesAsync(ct);
                return station;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                dbContext.Entry(station).State = EntityState.Detached;

                Station? existingStation = await FindExistingStationAfterInsertConflict(station, ct);
                if (existingStation is not null)
                {
                    return existingStation;
                }

                station.Id = Guid.NewGuid();
                station.StationGuid = Guid.NewGuid();
                station.StationCode = await StationCodeAllocator.GetNextCodeAsync(dbContext, ct);
                station.Name = StationCodeAllocator.CreateStationName(station.StationCode);
            }
        }

        throw new InvalidOperationException("Station registration failed after repeated unique key conflicts.");
    }

    private async Task<Station?> FindExistingStationAfterInsertConflict(Station station, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(station.HardwareId))
        {
            Station? stationByHardware = await dbContext.Stations.SingleOrDefaultAsync(item => item.HardwareId == station.HardwareId, ct);
            if (stationByHardware is not null)
            {
                return stationByHardware;
            }
        }

        if (station.StationGuid != Guid.Empty)
        {
            Station? stationByGuid = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == station.StationGuid, ct);
            if (stationByGuid is not null && !IsDifferentHardware(stationByGuid, station.HardwareId))
            {
                return stationByGuid;
            }
        }

        return null;
    }

    private static Station CreateStation(DeviceEventUploadRequest request, string stationCode, string hardwareId, Guid stationGuid)
        => new()
        {
            StationGuid = stationGuid == Guid.Empty ? Guid.NewGuid() : stationGuid,
            StationCode = stationCode,
            Name = stationCode,
            HardwareId = hardwareId,
            FirmwareVersion = Normalize(request.FirmwareVersion),
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

    private async Task UpdateStationMetadata(Station station, string requestedStationCode, bool generateStationCode, string hardwareId, string firmwareVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(station.StationCode) || StationCodeAllocator.ShouldGenerate(station.StationCode))
        {
            station.StationCode = generateStationCode
                ? await StationCodeAllocator.GetNextCodeAsync(dbContext, ct)
                : requestedStationCode;
        }

        if (StationCodeAllocator.HasGeneratedOrPlaceholderName(station))
        {
            station.Name = StationCodeAllocator.CreateStationName(station.StationCode);
        }

        if (!string.IsNullOrWhiteSpace(hardwareId) && string.IsNullOrWhiteSpace(station.HardwareId))
        {
            station.HardwareId = hardwareId;
        }

        if (!string.IsNullOrWhiteSpace(firmwareVersion))
        {
            station.FirmwareVersion = Normalize(firmwareVersion);
        }
    }

    private static bool IsDifferentHardware(Station station, string hardwareId)
        => !string.IsNullOrWhiteSpace(hardwareId)
            && !string.IsNullOrWhiteSpace(station.HardwareId)
            && !string.Equals(station.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

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

    private static DeviceEventDto MapEvent(DeviceEvent deviceEvent, Station station)
        => new()
        {
            Id = deviceEvent.Id,
            StationGuid = station.StationGuid,
            StationCode = station.StationCode,
            StationName = station.Name,
            SequenceNumber = deviceEvent.SequenceNumber,
            OccurredAt = deviceEvent.OccurredAt,
            Severity = deviceEvent.Severity,
            Category = deviceEvent.Category,
            Code = deviceEvent.Code,
            Component = deviceEvent.Component,
            Message = deviceEvent.Message
        };
}
