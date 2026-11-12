using System.Windows.Media;
using MeteoStanice.Monitor.Models;

namespace MeteoStanice.Monitor.ViewModels;

/// <summary>
/// Jednotne barvy aplikace. Barva nikdy nenese vyznam sama – vzdy je doplnena textem.
/// </summary>
public static class Palette
{
    /// <summary>Nizka koncentrace / v poradku.</summary>
    public static readonly Brush Good = Create("#2E7D32");

    /// <summary>Zvysena koncentrace / varovani.</summary>
    public static readonly Brush Elevated = Create("#E09A00");

    /// <summary>Vysoka koncentrace / chyba.</summary>
    public static readonly Brush High = Create("#C62828");

    /// <summary>Bezna hodnota velicin prostredi (neutralni, bez hodnoceni).</summary>
    public static readonly Brush Normal = Create("#1E6FB8");

    /// <summary>Hodnota neni k dispozici.</summary>
    public static readonly Brush Unknown = Create("#9AA5B1");

    /// <summary>Stanice nemeri.</summary>
    public static readonly Brush Offline = Create("#4A148C");

    /// <summary>Text na barevnem stitku.</summary>
    public static readonly Brush OnColor = Create("#FFFFFF");

    /// <summary>Pruhledne pozadi.</summary>
    public static readonly Brush None = Create("#00FFFFFF");

    /// <summary>
    /// Vytvori stetec z hex barvy; pri chybe vrati zalozni stetec.
    /// </summary>
    public static Brush FromHex(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        try
        {
            return Create(hex.Trim());
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Svetla (pruhledna) varianta barvy – podklad oblouku v barve veliciny.
    /// </summary>
    public static Brush Tint(Brush brush, byte alpha = 0x2E)
    {
        Color color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        SolidColorBrush tinted = new(Color.FromArgb(alpha, color.R, color.G, color.B));
        tinted.Freeze();
        return tinted;
    }

    /// <summary>
    /// Tmavsi odstin barvy (pro text v barve stanice na svetlem podkladu).
    /// </summary>
    public static Brush Darken(Brush brush, double amount = 0.35)
    {
        Color color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        double keep = 1.0 - Math.Clamp(amount, 0, 1);
        SolidColorBrush darker = new(Color.FromRgb((byte)(color.R * keep), (byte)(color.G * keep), (byte)(color.B * keep)));
        darker.Freeze();
        return darker;
    }

    /// <summary>Barva podle stavu stanice.</summary>
    public static Brush ForHealth(StationHealth health) => health switch
    {
        StationHealth.Ok => Good,
        StationHealth.Warning => Elevated,
        StationHealth.Error => High,
        _ => Offline
    };

    private static Brush Create(string hex)
    {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
