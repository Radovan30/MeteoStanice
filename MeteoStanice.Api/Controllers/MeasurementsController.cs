using MeteoStanice.Api.Services;
using MeteoStanice.Contracts.Measurements;
using MeteoStanice.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Controllers;

/// <summary>
/// Exposes ten-minute aggregates. They are computed by the backend from raw samples
/// (<see cref="SensorSampleAggregationService"/>); stations no longer upload aggregates.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class MeasurementsController(MeteoDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Returns recent measurements for one station, newest first.
    /// </summary>
    [HttpGet("{stationGuid:guid}/recent")]
    public async Task<ActionResult<IReadOnlyList<MeasurementDto>>> GetRecent(Guid stationGuid, int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        var rows = await dbContext.Measurements
            .AsNoTracking()
            .Where(item => item.Station.StationGuid == stationGuid)
            .OrderByDescending(item => item.MeasuredAt)
            .Take(take)
            .Select(item => new { Measurement = item, item.Station, FlagCount = item.QualityFlags.Count })
            .ToListAsync(ct);

        return rows
            .Select(row => MeasurementMapper.ToDto(row.Measurement, row.Station, row.FlagCount))
            .ToList();
    }
}
