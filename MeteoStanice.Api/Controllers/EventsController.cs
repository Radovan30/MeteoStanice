using MeteoStanice.Api.Security;
using MeteoStanice.Api.Services;
using MeteoStanice.Contracts.Events;
using MeteoStanice.Contracts.Protocol;
using MeteoStanice.Contracts.Statuses;
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
public sealed class EventsController(
    MeteoDbContext dbContext,
    StationRegistrationService registrationService) : ControllerBase
{
    /// <summary>
    /// Stores one device event uploaded by station firmware (idempotent by EventGuid).
    /// </summary>
    [HttpPost]
    [StationApiKey]
    public async Task<ActionResult<DeviceEventDto>> Upload(DeviceEventUploadRequest request, CancellationToken ct)
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

        string stationCode = !string.IsNullOrWhiteSpace(request.StationCode) ? request.StationCode : request.StationId;
        Station station = await registrationService.GetOrCreateAsync(
            new StationUploadIdentity(request.StationGuid, stationCode, request.HardwareId, request.FirmwareVersion, request.ProtocolVersion),
            ct);

        DeviceEvent? existingEvent = request.EventGuid != Guid.Empty
            ? await dbContext.DeviceEvents.SingleOrDefaultAsync(
                item => item.StationId == station.Id && item.EventGuid == request.EventGuid, ct)
            : await dbContext.DeviceEvents
                .Where(item => item.StationId == station.Id && item.SequenceNumber == request.SequenceNumber)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(ct);
        if (existingEvent is not null)
        {
            await dbContext.SaveChangesAsync(ct);
            return Ok(MapEvent(existingEvent, station));
        }

        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
        DeviceEvent deviceEvent = new()
        {
            Station = station,
            EventGuid = request.EventGuid == Guid.Empty ? Guid.NewGuid() : request.EventGuid,
            SequenceNumber = request.SequenceNumber,
            // An event created before the station clock was synchronized carries a time around 1970;
            // such a time is replaced by the receive time (documented limitation, CreatedAt keeps the receive time too).
            OccurredAt = UploadTimeValidator.IsPlausible(request.OccurredAt, receivedAt) ? request.OccurredAt.ToUniversalTime() : receivedAt,
            Severity = FirmwareStatusCatalog.NormalizeEventSeverity(request.Severity),
            Category = FirmwareStatusCatalog.NormalizeEventCategory(request.Category),
            Code = FirmwareStatusCatalog.NormalizeEventCode(request.Code),
            Component = Truncate(request.Component, 120),
            Message = Truncate(request.Message, 500),
            CreatedAt = receivedAt
        };

        station.LastSeenAt = receivedAt;
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
                EventGuid = item.EventGuid,
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

        foreach (DeviceEventDto deviceEvent in events)
        {
            NormalizeEvent(deviceEvent);
        }

        return events;
    }

    private static string Truncate(string? value, int maximumLength)
    {
        string text = value ?? string.Empty;
        return text.Length > maximumLength ? text[..maximumLength] : text;
    }

    private static DeviceEventDto MapEvent(DeviceEvent deviceEvent, Station station)
        => new()
        {
            Id = deviceEvent.Id,
            EventGuid = deviceEvent.EventGuid,
            StationGuid = station.StationGuid,
            StationCode = station.StationCode,
            StationName = station.Name,
            SequenceNumber = deviceEvent.SequenceNumber,
            OccurredAt = deviceEvent.OccurredAt,
            Severity = FirmwareStatusCatalog.NormalizeEventSeverity(deviceEvent.Severity),
            Category = FirmwareStatusCatalog.NormalizeEventCategory(deviceEvent.Category),
            Code = FirmwareStatusCatalog.NormalizeEventCode(deviceEvent.Code),
            Component = deviceEvent.Component,
            Message = deviceEvent.Message
        };

    private static void NormalizeEvent(DeviceEventDto deviceEvent)
    {
        deviceEvent.Severity = FirmwareStatusCatalog.NormalizeEventSeverity(deviceEvent.Severity);
        deviceEvent.Category = FirmwareStatusCatalog.NormalizeEventCategory(deviceEvent.Category);
        deviceEvent.Code = FirmwareStatusCatalog.NormalizeEventCode(deviceEvent.Code);
    }
}
