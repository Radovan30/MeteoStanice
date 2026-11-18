namespace MeteoStanice.Api.Services;

/// <summary>
/// Rejects timestamps produced by a station whose clock was not synchronized from NTP.
/// </summary>
public static class UploadTimeValidator
{
    /// <summary>
    /// Earliest accepted measurement time; the system did not exist before.
    /// </summary>
    public static readonly DateTimeOffset MinimumValidTime = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Tolerated clock skew of a station ahead of the server.
    /// </summary>
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);

    public static bool IsPlausible(DateTimeOffset timestamp, DateTimeOffset serverNow)
        => timestamp != default
            && timestamp >= MinimumValidTime
            && timestamp <= serverNow.Add(MaximumFutureSkew);
}
