using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Services;

/// <summary>
/// Closes the previous ten-minute window even when a station stops sending after its last sample.
/// </summary>
public sealed class SensorSampleAggregationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SensorSampleAggregationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BoundaryDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RebuildPreviousIntervalAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Periodic sensor sample aggregation failed.");
            }

            await Task.Delay(GetDelayUntilNextBoundary(), stoppingToken);
        }
    }

    private async Task RebuildPreviousIntervalAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        MeteoDbContext dbContext = scope.ServiceProvider.GetRequiredService<MeteoDbContext>();
        SensorSampleAggregationService aggregationService = scope.ServiceProvider.GetRequiredService<SensorSampleAggregationService>();

        List<Station> stations = await dbContext.Stations
            .Where(item => item.IsActive)
            .ToListAsync(ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (Station station in stations)
        {
            await aggregationService.RebuildAffectedIntervalsAsync(station, [now], ct);
        }
    }

    private static TimeSpan GetDelayUntilNextBoundary()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long intervalTicks = Interval.Ticks;
        long nextTicks = now.Ticks - (now.Ticks % intervalTicks) + intervalTicks;
        TimeSpan delay = new DateTimeOffset(nextTicks, TimeSpan.Zero).Add(BoundaryDelay) - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
    }
}
