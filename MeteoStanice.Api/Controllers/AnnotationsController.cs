using System.Globalization;
using System.Text;
using MeteoStanice.Api.Security;
using MeteoStanice.Contracts.Annotations;
using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Controllers;

/// <summary>
/// Intervention log and reference annotations. Reading is public on the LAN,
/// writing requires the administrator key (X-Admin-Key).
/// </summary>
[ApiController]
[Route("api/annotations")]
public sealed class AnnotationsController(MeteoDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Returns annotations overlapping the time range, optionally for one station
    /// (annotations valid for all stations are always included).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StationAnnotationDto>>> Get(
        Guid? stationGuid = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        List<StationAnnotation> items = await Query(stationGuid, from, to).ToListAsync(ct);
        return items.Select(Map).ToList();
    }

    /// <summary>
    /// Allowed categories.
    /// </summary>
    [HttpGet("categories")]
    public ActionResult<IReadOnlyList<string>> GetCategories() => Ok(AnnotationCategories.All);

    /// <summary>
    /// Exports annotations as CSV for the analysis (ground truth for detector evaluation).
    /// </summary>
    [HttpGet("export.csv")]
    public async Task ExportCsv(
        Guid? stationGuid = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        List<StationAnnotation> items = await Query(stationGuid, from, to).ToListAsync(ct);
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition =
            $"attachment; filename=meteostanice-annotations-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

        await using StreamWriter writer = new(Response.Body, new UTF8Encoding(false), 16 * 1024, leaveOpen: true);
        await writer.WriteLineAsync("Id,StationCode,StartsAtUtc,EndsAtUtc,Category,IsAnomaly,AffectedChannels,Title,Description,Author");
        foreach (StationAnnotation item in items)
        {
            await writer.WriteLineAsync(string.Join(",",
                Csv(item.Id.ToString()),
                Csv(item.Station?.StationCode ?? string.Empty),
                Csv(item.StartsAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                Csv(item.EndsAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                Csv(item.Category),
                item.IsAnomaly ? "true" : "false",
                Csv(item.AffectedChannels),
                Csv(item.Title),
                Csv(item.Description),
                Csv(item.Author)));
        }

        await writer.FlushAsync(ct);
    }

    [HttpPost]
    [AdminApiKey]
    public async Task<ActionResult<StationAnnotationDto>> Create(StationAnnotationRequest request, CancellationToken ct)
    {
        StationAnnotation annotation = new() { CreatedAt = DateTimeOffset.UtcNow };
        ActionResult? error = await ApplyAsync(annotation, request, ct);
        if (error is not null)
        {
            return error;
        }

        dbContext.StationAnnotations.Add(annotation);
        await dbContext.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { }, Map(annotation));
    }

    [HttpPut("{id:guid}")]
    [AdminApiKey]
    public async Task<ActionResult<StationAnnotationDto>> Update(Guid id, StationAnnotationRequest request, CancellationToken ct)
    {
        StationAnnotation? annotation = await dbContext.StationAnnotations
            .Include(item => item.Station)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (annotation is null)
        {
            return NotFound();
        }

        ActionResult? error = await ApplyAsync(annotation, request, ct);
        if (error is not null)
        {
            return error;
        }

        annotation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return Map(annotation);
    }

    [HttpDelete("{id:guid}")]
    [AdminApiKey]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        int deleted = await dbContext.StationAnnotations.Where(item => item.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }

    private IQueryable<StationAnnotation> Query(Guid? stationGuid, DateTimeOffset? from, DateTimeOffset? to)
    {
        IQueryable<StationAnnotation> query = dbContext.StationAnnotations.AsNoTracking().Include(item => item.Station);
        if (stationGuid.HasValue)
        {
            query = query.Where(item => item.StationId == null || item.Station!.StationGuid == stationGuid.Value);
        }

        if (to.HasValue)
        {
            DateTimeOffset toUtc = to.Value.ToUniversalTime();
            query = query.Where(item => item.StartsAt < toUtc);
        }

        if (from.HasValue)
        {
            DateTimeOffset fromUtc = from.Value.ToUniversalTime();
            query = query.Where(item => item.EndsAt == null || item.EndsAt >= fromUtc);
        }

        return query.OrderBy(item => item.StartsAt);
    }

    private async Task<ActionResult?> ApplyAsync(StationAnnotation annotation, StationAnnotationRequest request, CancellationToken ct)
    {
        string category = AnnotationCategories.All.FirstOrDefault(item => string.Equals(item, request.Category?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        if (category.Length == 0)
        {
            return BadRequest(new { message = "Unknown category.", allowed = AnnotationCategories.All });
        }

        if (request.StartsAt == default)
        {
            return BadRequest(new { message = "StartsAt is required." });
        }

        if (request.EndsAt.HasValue && request.EndsAt.Value < request.StartsAt)
        {
            return BadRequest(new { message = "EndsAt must not be earlier than StartsAt." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Title is required." });
        }

        Station? station = null;
        if (request.StationGuid.HasValue)
        {
            station = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == request.StationGuid.Value, ct);
            if (station is null)
            {
                return BadRequest(new { message = "Unknown station." });
            }
        }

        annotation.Station = station;
        annotation.StationId = station?.Id;
        annotation.StartsAt = request.StartsAt.ToUniversalTime();
        annotation.EndsAt = request.EndsAt?.ToUniversalTime();
        annotation.Category = category;
        annotation.Title = Truncate(request.Title, 160);
        annotation.Description = Truncate(request.Description, 2000);
        annotation.AffectedChannels = Truncate(request.AffectedChannels?.Replace(" ", string.Empty), 200);
        annotation.IsAnomaly = request.IsAnomaly ?? AnnotationCategories.IsAnomalyByDefault(category);
        annotation.Author = Truncate(request.Author, 80);
        return null;
    }

    private static StationAnnotationDto Map(StationAnnotation item)
        => new()
        {
            Id = item.Id,
            StationGuid = item.Station?.StationGuid,
            StationCode = item.Station?.StationCode ?? string.Empty,
            StartsAt = item.StartsAt,
            EndsAt = item.EndsAt,
            Category = item.Category,
            Title = item.Title,
            Description = item.Description,
            AffectedChannels = item.AffectedChannels,
            IsAnomaly = item.IsAnomaly,
            Author = item.Author,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };

    private static string Truncate(string? value, int maximumLength)
    {
        string text = value?.Trim() ?? string.Empty;
        return text.Length > maximumLength ? text[..maximumLength] : text;
    }

    private static string Csv(string? value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
