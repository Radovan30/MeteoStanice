using System.IO;
using System.Text.Json;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Ulozeny stav okna mezi spustenimi aplikace.
/// </summary>
public sealed class UiState
{
    /// <summary>Okno bylo pripnute na plose jako widget.</summary>
    public bool WidgetMode { get; set; }

    /// <summary>Poloha leveho okraje okna (DIP).</summary>
    public double? Left { get; set; }

    /// <summary>Poloha horniho okraje okna (DIP).</summary>
    public double? Top { get; set; }

    /// <summary>Kody skrytych stanic.</summary>
    public List<string> HiddenStations { get; set; } = [];
}

/// <summary>
/// Cteni a zapis stavu okna do %LOCALAPPDATA%\MeteoStanice.Monitor\ui-state.json.
/// Chyby se ignoruji – stav je jen pohodli uzivatele.
/// </summary>
public static class UiStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeteoStanice.Monitor",
        "ui-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Nacte ulozeny stav (nebo vychozi).
    /// </summary>
    public static UiState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UiState>(File.ReadAllText(FilePath), JsonOptions) ?? new UiState();
            }
        }
        catch (Exception)
        {
            // poskozeny soubor – pouzije se vychozi stav
        }

        return new UiState();
    }

    /// <summary>
    /// Ulozi stav.
    /// </summary>
    public static void Save(UiState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception)
        {
            // nelze zapsat – stav se neulozi
        }
    }
}
