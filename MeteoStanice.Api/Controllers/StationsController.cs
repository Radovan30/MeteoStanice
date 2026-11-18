using MeteoStanice.Api.Security;
using MeteoStanice.Contracts.Stations;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Controllers;

[ApiController]
[Route("api/stations")]
public sealed class StationsController(MeteoDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StationDto>>> GetAll(CancellationToken ct = default)
    {
        List<StationDto> stations = await dbContext.Stations
            .AsNoTracking()
            .OrderBy(item => item.StationCode)
            .Select(item => new StationDto
            {
                StationGuid = item.StationGuid,
                StationCode = item.StationCode,
                Name = item.Name,
                LocationText = item.LocationText,
                HardwareId = item.HardwareId,
                FirmwareVersion = item.FirmwareVersion,
                ProtocolVersion = item.ProtocolVersion,
                Sps30CorrectionPercent = item.Sps30CorrectionPercent,
                LastSeenAt = item.LastSeenAt,
                IsActive = item.IsActive
            })
            .ToListAsync(ct);

        return stations;
    }

    /// <summary>
    /// Sets the signed SPS30 percentage correction of one station. Raw values are never changed;
    /// the correction is applied when measurements are read (Corrected* fields).
    /// </summary>
    [HttpPut("{stationGuid:guid}/sps30-correction")]
    [AdminApiKey]
    public async Task<ActionResult<StationDto>> UpdateSps30Correction(
        Guid stationGuid,
        UpdateSps30CorrectionRequest request,
        CancellationToken ct)
    {
        if (double.IsNaN(request.CorrectionPercent)
            || double.IsInfinity(request.CorrectionPercent)
            || request.CorrectionPercent < -100
            || request.CorrectionPercent > 500)
        {
            return BadRequest(new
            {
                message = "CorrectionPercent must be a finite value between -100 and 500."
            });
        }

        Station? station = await dbContext.Stations
            .SingleOrDefaultAsync(item => item.StationGuid == stationGuid, ct);
        if (station is null)
        {
            return NotFound();
        }

        station.Sps30CorrectionPercent = request.CorrectionPercent;
        await dbContext.SaveChangesAsync(ct);

        return Map(station);
    }

    private static StationDto Map(Station station)
        => new()
        {
            StationGuid = station.StationGuid,
            StationCode = station.StationCode,
            Name = station.Name,
            LocationText = station.LocationText,
            HardwareId = station.HardwareId,
            FirmwareVersion = station.FirmwareVersion,
            ProtocolVersion = station.ProtocolVersion,
            Sps30CorrectionPercent = station.Sps30CorrectionPercent,
            LastSeenAt = station.LastSeenAt,
            IsActive = station.IsActive
        };
}
