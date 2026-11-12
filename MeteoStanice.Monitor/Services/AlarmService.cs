using System.IO;
using System.Media;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Sleduje aktivni alarmy, jejich potvrzeni a prehrava zvukove upozorneni.
/// Novy alarm (klic, ktery predtim nebyl aktivni) zvuk spusti okamzite; nepotvrzene alarmy se opakuji.
/// </summary>
public sealed class AlarmService
{
    private readonly SoundOptions _options;
    private readonly SoundPlayer? _player;
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    private HashSet<string> _current = new(StringComparer.Ordinal);
    private DateTime _lastPlayedUtc = DateTime.MinValue;

    /// <summary>
    /// Vytvori sluzbu a pripadne nacte WAV soubor.
    /// </summary>
    public AlarmService(SoundOptions options)
    {
        _options = options;
        Enabled = options.Enabled;

        if (!string.IsNullOrWhiteSpace(options.WavFile))
        {
            string path = Path.IsPathRooted(options.WavFile)
                ? options.WavFile
                : Path.Combine(AppContext.BaseDirectory, options.WavFile);
            if (File.Exists(path))
            {
                try
                {
                    _player = new SoundPlayer(path);
                    _player.Load();
                }
                catch (Exception)
                {
                    _player = null; // fallback na systemovy zvuk
                }
            }
        }
    }

    /// <summary>
    /// Zvuk zapnut/vypnut uzivatelem.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Existuje alespon jeden nepotvrzeny alarm.
    /// </summary>
    public bool HasUnacknowledged => _current.Any(key => !_acknowledged.Contains(key));

    /// <summary>
    /// Pocet nepotvrzenych alarmu.
    /// </summary>
    public int UnacknowledgedCount => _current.Count(key => !_acknowledged.Contains(key));

    /// <summary>
    /// Nastavi aktualni mnozinu alarmu. Vraci true, pokud pribyl novy nepotvrzeny alarm.
    /// </summary>
    public bool Update(IEnumerable<string> activeKeys)
    {
        HashSet<string> next = new(activeKeys, StringComparer.Ordinal);

        // Alarm, ktery zmizel, se pri opetovnem vzniku ohlasi znovu.
        _acknowledged.IntersectWith(next);
        bool hasNew = next.Any(key => !_current.Contains(key) && !_acknowledged.Contains(key));
        _current = next;
        return hasNew;
    }

    /// <summary>
    /// Potvrdi (ztlumi) vsechny aktualne aktivni alarmy.
    /// </summary>
    public void Acknowledge() => _acknowledged.UnionWith(_current);

    /// <summary>
    /// Prehraje zvuk, pokud je co hlasit a uplynula perioda opakovani (nebo force).
    /// </summary>
    public void Tick(bool force = false)
    {
        if (!Enabled || !HasUnacknowledged)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (!force && now - _lastPlayedUtc < TimeSpan.FromSeconds(_options.RepeatSeconds))
        {
            return;
        }

        _lastPlayedUtc = now;
        Play();
    }

    /// <summary>
    /// Testovaci prehrani zvuku.
    /// </summary>
    public void Play()
    {
        try
        {
            if (_player is not null)
            {
                _player.Play();
            }
            else
            {
                SystemSounds.Hand.Play();
            }
        }
        catch (Exception)
        {
            SystemSounds.Hand.Play();
        }
    }
}
