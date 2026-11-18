using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Services;

/// <summary>
/// Identity fields sent by station firmware with every upload.
/// </summary>
public sealed record StationUploadIdentity(
    Guid StationGuid,
    string? StationCode,
    string? HardwareId,
    string? FirmwareVersion,
    int ProtocolVersion,
    string? LocationText = null);

/// <summary>
/// Finds or registers the physical station behind an upload. Shared by the sample and event endpoints,
/// so both resolve the same station in the same way (hardware ID, then station GUID, then station code).
/// </summary>
public sealed class StationRegistrationService(MeteoDbContext dbContext)
{
    private const int MaximumInsertAttempts = 5;
    private const int LocationTextMaxLength = 200;

    /// <summary>
    /// Returns a tracked station. A new station is saved immediately; changes of an existing
    /// station (code, firmware, protocol, location) are saved by the caller's SaveChanges.
    /// </summary>
    public async Task<Station> GetOrCreateAsync(StationUploadIdentity identity, CancellationToken ct)
    {
        string requestedCode = Normalize(identity.StationCode);
        if (requestedCode.Length == 0 && identity.StationGuid != Guid.Empty)
        {
            requestedCode = identity.StationGuid.ToString();
        }

        string hardwareId = Normalize(identity.HardwareId);
        bool generateCode = StationCodeAllocator.ShouldGenerate(requestedCode);

        if (hardwareId.Length > 0)
        {
            Station? byHardware = await dbContext.Stations.SingleOrDefaultAsync(item => item.HardwareId == hardwareId, ct);
            if (byHardware is not null)
            {
                await UpdateMetadataAsync(byHardware, requestedCode, generateCode, hardwareId, identity, ct);
                return byHardware;
            }
        }

        if (identity.StationGuid != Guid.Empty)
        {
            Station? byGuid = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == identity.StationGuid, ct);
            if (byGuid is not null)
            {
                if (IsDifferentHardware(byGuid, hardwareId))
                {
                    // The GUID belongs to another board (for example a copied configuration): new identity.
                    string collisionCode = await StationCodeAllocator.GetNextCodeAsync(dbContext, ct);
                    return await SaveNewStationAsync(CreateStation(identity, collisionCode, hardwareId, Guid.NewGuid()), ct);
                }

                await UpdateMetadataAsync(byGuid, requestedCode, generateCode, hardwareId, identity, ct);
                return byGuid;
            }
        }

        if (!generateCode)
        {
            Station? byCode = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationCode == requestedCode, ct);
            if (byCode is not null)
            {
                if (IsDifferentHardware(byCode, hardwareId))
                {
                    string collisionCode = await StationCodeAllocator.GetNextCodeAsync(dbContext, ct);
                    return await SaveNewStationAsync(CreateStation(identity, collisionCode, hardwareId, Guid.NewGuid()), ct);
                }

                await UpdateMetadataAsync(byCode, requestedCode, generateCode, hardwareId, identity, ct);
                return byCode;
            }
        }

        string stationCode = generateCode
            ? await StationCodeAllocator.GetNextCodeAsync(dbContext, ct)
            : requestedCode;
        return await SaveNewStationAsync(CreateStation(identity, stationCode, hardwareId, identity.StationGuid), ct);
    }

    private async Task<Station> SaveNewStationAsync(Station station, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaximumInsertAttempts; attempt++)
        {
            try
            {
                dbContext.Stations.Add(station);
                await dbContext.SaveChangesAsync(ct);
                return station;
            }
            catch (DbUpdateException) when (attempt < MaximumInsertAttempts)
            {
                // Concurrent registration of the same board or a code collision.
                dbContext.Entry(station).State = EntityState.Detached;
                Station? existing = await FindExistingAfterConflictAsync(station, ct);
                if (existing is not null)
                {
                    return existing;
                }

                station.Id = Guid.NewGuid();
                station.StationGuid = Guid.NewGuid();
                station.StationCode = await StationCodeAllocator.GetNextCodeAsync(dbContext, ct);
                station.Name = StationCodeAllocator.CreateStationName(station.StationCode);
            }
        }

        throw new InvalidOperationException("Station registration failed after repeated unique key conflicts.");
    }

    private async Task<Station?> FindExistingAfterConflictAsync(Station station, CancellationToken ct)
    {
        if (station.HardwareId.Length > 0)
        {
            Station? byHardware = await dbContext.Stations.SingleOrDefaultAsync(item => item.HardwareId == station.HardwareId, ct);
            if (byHardware is not null)
            {
                return byHardware;
            }
        }

        Station? byGuid = await dbContext.Stations.SingleOrDefaultAsync(item => item.StationGuid == station.StationGuid, ct);
        return byGuid is not null && !IsDifferentHardware(byGuid, station.HardwareId) ? byGuid : null;
    }

    private static Station CreateStation(StationUploadIdentity identity, string stationCode, string hardwareId, Guid stationGuid)
        => new()
        {
            StationGuid = stationGuid == Guid.Empty ? Guid.NewGuid() : stationGuid,
            StationCode = stationCode,
            Name = StationCodeAllocator.CreateStationName(stationCode),
            HardwareId = hardwareId,
            FirmwareVersion = Normalize(identity.FirmwareVersion),
            ProtocolVersion = identity.ProtocolVersion,
            LocationText = NormalizeLocation(identity.LocationText),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

    private async Task UpdateMetadataAsync(Station station, string requestedCode, bool generateCode, string hardwareId, StationUploadIdentity identity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(station.StationCode) || StationCodeAllocator.ShouldGenerate(station.StationCode))
        {
            station.StationCode = generateCode
                ? await StationCodeAllocator.GetNextCodeAsync(dbContext, ct)
                : requestedCode;
        }

        if (StationCodeAllocator.HasGeneratedOrPlaceholderName(station))
        {
            station.Name = StationCodeAllocator.CreateStationName(station.StationCode);
        }

        if (hardwareId.Length > 0 && string.IsNullOrWhiteSpace(station.HardwareId))
        {
            station.HardwareId = hardwareId;
        }

        if (!string.IsNullOrWhiteSpace(identity.FirmwareVersion))
        {
            station.FirmwareVersion = Normalize(identity.FirmwareVersion);
        }

        string location = NormalizeLocation(identity.LocationText);
        if (location.Length > 0)
        {
            station.LocationText = location;
        }

        station.ProtocolVersion = identity.ProtocolVersion;
    }

    private static bool IsDifferentHardware(Station station, string hardwareId)
        => hardwareId.Length > 0
            && !string.IsNullOrWhiteSpace(station.HardwareId)
            && !string.Equals(station.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeLocation(string? value)
    {
        string location = Normalize(value);
        return location.Length > LocationTextMaxLength ? location[..LocationTextMaxLength] : location;
    }
}
