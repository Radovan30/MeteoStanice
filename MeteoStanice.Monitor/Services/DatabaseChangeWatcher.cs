using System.Text.RegularExpressions;
using Npgsql;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Push upozorneni na zmeny v DB pres PostgreSQL LISTEN/NOTIFY (Npgsql) – obdoba SqlDependency u SQL Serveru.
/// Databaze posle notifikaci triggerem z Database/monitor-notify.sql; aplikace se nic nedotazuje periodicky.
/// Spojeni se pri vypadku automaticky obnovi.
/// </summary>
public sealed partial class DatabaseChangeWatcher : IAsyncDisposable
{
    private const string TriggerName = "meteo_monitor_notify";
    private const string TriggerCheckSql = "SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = @name)";

    // Jak casto overit, ze spojeni zije a trigger v DB stale existuje (bez cteni dat).
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(1);

    private readonly string _connectionString;
    private readonly string _channel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    /// <summary>
    /// Vytvori hlidani zmen.
    /// </summary>
    public DatabaseChangeWatcher(string connectionString, string channel)
    {
        if (!ChannelNameRegex().IsMatch(channel))
        {
            throw new ArgumentException("NotifyChannel smí obsahovat jen malá písmena, číslice a podtržítko.", nameof(channel));
        }

        // KeepAlive: vypadek site se u cekajiciho spojeni projevi vyjimkou a spojeni se obnovi.
        NpgsqlConnectionStringBuilder builder = new(connectionString) { KeepAlive = 30 };
        _connectionString = builder.ConnectionString;
        _channel = channel;
    }

    /// <summary>
    /// Data v DB se zmenila (nebo bylo obnoveno spojeni). Vola se z pracovniho vlakna.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Zmena stavu: true = notifikace aktivni (spojeni + trigger), text pro uzivatele. Vola se z pracovniho vlakna.
    /// </summary>
    public event Action<bool, string>? StatusChanged;

    /// <summary>
    /// Spusti naslouchani na pozadi.
    /// </summary>
    public void Start() => _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                // ukonceni aplikace – chyby se ignoruji
            }
        }

        _cts.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using NpgsqlConnection connection = new(_connectionString);
                await connection.OpenAsync(ct);

                bool triggerInstalled = await IsTriggerInstalledAsync(connection, ct);

                connection.Notification += (_, _) => Changed?.Invoke();
                await using (NpgsqlCommand listen = new($"LISTEN {_channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(ct);
                }

                ReportTrigger(triggerInstalled);

                // Po (znovu)pripojeni nacist data – behem vypadku mohla prijit zmena.
                Changed?.Invoke();

                while (!ct.IsCancellationRequested)
                {
                    // Cekani na notifikaci s casovym limitem. Pri vyprseni se overi, ze spojeni zije
                    // a trigger existuje (napr. po obnove DB nebo migraci) – mrtve spojeni vyhodi vyjimku a obnovi se.
                    bool notified = await connection.WaitAsync((int)HealthCheckInterval.TotalMilliseconds, ct);
                    if (!notified)
                    {
                        bool installed = await IsTriggerInstalledAsync(connection, ct);
                        if (installed != triggerInstalled)
                        {
                            triggerInstalled = installed;
                            ReportTrigger(installed);
                            Changed?.Invoke();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                StatusChanged?.Invoke(false, "spojení pro notifikace přerušeno – obnovuji…");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<bool> IsTriggerInstalledAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using NpgsqlCommand check = new(TriggerCheckSql, connection);
        check.Parameters.AddWithValue("name", TriggerName);
        return await check.ExecuteScalarAsync(ct) is true;
    }

    private void ReportTrigger(bool triggerInstalled)
        => StatusChanged?.Invoke(
            triggerInstalled,
            triggerInstalled
                ? "obnovení při změně v DB (LISTEN/NOTIFY)"
                : "trigger v DB chybí – spusťte Database/monitor-notify.sql; zatím záložní obnovení");

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex ChannelNameRegex();
}
