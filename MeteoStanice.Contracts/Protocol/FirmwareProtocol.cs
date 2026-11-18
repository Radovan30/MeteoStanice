namespace MeteoStanice.Contracts.Protocol;

/// <summary>
/// Version of the JSON protocol shared by station firmware and backend.
/// 0 = legacy firmware without the field, 1 = raw 10 s samples (firmware 0.3.x),
/// 2 = adds SPS30 number concentrations and station location text (firmware 0.4.x).
/// </summary>
public static class FirmwareProtocol
{
    public const int LegacyVersion = 0;
    public const int CurrentVersion = 2;

    public static bool IsSupported(int version)
        => version >= LegacyVersion && version <= CurrentVersion;
}
