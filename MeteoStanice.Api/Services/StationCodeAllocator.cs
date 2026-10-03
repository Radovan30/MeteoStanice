using MeteoStanice.Data;
using MeteoStanice.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Api.Services;

public static class StationCodeAllocator
{
    private const string GeneratedPrefix = "meteostanice-";
    private const string UnknownStationCode = "METEO-UNKNOWN";
    private const string TestStationCodePrefix = "METEO-TEST-";

    public static bool ShouldGenerate(string? stationCode)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
        {
            return true;
        }

        string trimmed = stationCode.Trim();
        return trimmed.Equals(UnknownStationCode, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(TestStationCodePrefix, StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(trimmed, out _);
    }

    public static async Task<string> GetNextCodeAsync(MeteoDbContext dbContext, CancellationToken ct)
    {
        List<string> stationCodes = await dbContext.Stations
            .Select(station => station.StationCode)
            .Where(stationCode => stationCode.StartsWith(GeneratedPrefix))
            .ToListAsync(ct);

        HashSet<string> existingCodes = new(stationCodes, StringComparer.OrdinalIgnoreCase);
        int maxNumber = 0;
        foreach (string stationCode in stationCodes)
        {
            string suffix = stationCode[GeneratedPrefix.Length..];
            if (int.TryParse(suffix, out int number) && number > maxNumber)
            {
                maxNumber = number;
            }
        }

        int nextNumber = maxNumber + 1;
        string nextCode;
        do
        {
            nextCode = $"{GeneratedPrefix}{nextNumber:000}";
            nextNumber++;
        }
        while (existingCodes.Contains(nextCode));

        return nextCode;
    }

    public static bool HasGeneratedOrPlaceholderName(Station station)
        => string.IsNullOrWhiteSpace(station.Name)
            || station.Name.Equals(station.StationCode, StringComparison.OrdinalIgnoreCase)
            || ShouldGenerate(station.Name);

    public static string CreateStationName(string stationCode)
        => stationCode;
}
