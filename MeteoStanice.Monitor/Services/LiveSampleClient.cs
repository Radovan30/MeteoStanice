using System.Net.Http.Json;
using MeteoStanice.Contracts.Samples;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Reads the latest raw 10 s sample of every station from <c>GET /api/samples/latest</c>.
/// Read-only; uses no API key because the GET endpoints are public on the LAN.
/// </summary>
public sealed class LiveSampleClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _latestUri;

    public LiveSampleClient(string apiBaseUrl)
    {
        string baseUrl = apiBaseUrl.Trim().TrimEnd('/');
        _latestUri = new Uri(baseUrl + "/samples/latest");
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>
    /// Address of the endpoint (for status texts).
    /// </summary>
    public string Endpoint => _latestUri.ToString();

    public async Task<IReadOnlyList<SensorSampleDto>> ReadLatestAsync(CancellationToken ct)
    {
        List<SensorSampleDto>? samples = await _httpClient.GetFromJsonAsync<List<SensorSampleDto>>(_latestUri, ct);
        return samples ?? [];
    }

    public void Dispose() => _httpClient.Dispose();
}
