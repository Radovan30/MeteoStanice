using System.Globalization;
using MeteoStanice.Data.Entities;
using MeteoStanice.Monitor.Models;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Pravidlove vyhodnoceni provozniho stavu stanice z posledniho mereni, jeho priznaku kvality a udalosti zarizeni.
/// Nevyhodnocuje anomalie v datech – jen to, zda stanice meri a zda hlasi chyby.
/// </summary>
public static class StationHealthEvaluator
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    // Merici rozsah SPS30 dle datasheetu Sensirion: hmotnostni koncentrace 0–1000 µg/m³,
    // pocetni koncentrace 0–3000 #/cm³. Hodnota mimo rozsah neni platne mereni, ale porucha/chyba cteni.
    private const double Sps30MassMaximum = 1000;
    private const double Sps30NumberMaximum = 3000;

    /// <summary>
    /// Vyhodnoti stav jedne stanice.
    /// </summary>
    public static StationEvaluation Evaluate(StationSnapshot snapshot, MonitorOptions options, DateTimeOffset now)
    {
        List<StationIssue> issues = [];
        List<SensorProblem> problems = [];
        Measurement? measurement = snapshot.LatestMeasurement;
        int intervalMinutes = snapshot.MeasurementIntervalMinutes > 0
            ? snapshot.MeasurementIntervalMinutes
            : options.DefaultMeasurementIntervalMinutes;
        TimeSpan offlineLimit = TimeSpan.FromMinutes(intervalMinutes * options.OfflineAfterIntervals);
        bool stale = false;

        if (measurement is null)
        {
            stale = true;
            issues.Add(new("NoData", StationHealth.Offline, "Stanice zatím neodeslala žádné měření."));
        }
        else
        {
            TimeSpan age = now - measurement.MeasuredAt;
            if (age > offlineLimit)
            {
                stale = true;
                bool communicates = snapshot.Station.LastSeenAt is { } lastSeen && now - lastSeen <= offlineLimit;
                string message = communicates
                    ? $"Stanice komunikuje, ale {FormatAge(age)} neposlala nové měření."
                    : $"Žádná nová data {FormatAge(age)} – stanice neměří nebo je nedostupná.";
                issues.Add(new("Offline", StationHealth.Offline, message));
            }
            else if (age < TimeSpan.FromMinutes(-5))
            {
                issues.Add(new("FutureTime", StationHealth.Warning, "Čas měření je v budoucnosti – zkontrolujte synchronizaci času (NTP)."));
            }

            if (snapshot.FutureMeasurementCount > 0)
            {
                issues.Add(new(
                    "FutureRecords",
                    StationHealth.Warning,
                    $"V DB je {snapshot.FutureMeasurementCount}× měření s časem v budoucnosti – ignorováno (chybný čas stanice?)."));
            }

            string status = FirmwareEnums.Normalize(measurement.Status, FirmwareEnums.MeasurementStatusNames);
            bool sps30OffOnBattery = IsSps30OffOnBattery(measurement, status);
            EvaluateStatus(status, issues);
            EvaluateSps30(measurement, status, sps30OffOnBattery, issues);
            if (!sps30OffOnBattery)
            {
                EvaluateSps30Range(measurement, issues, problems);
            }
            EvaluateFlags(snapshot.LatestFlags, issues, problems);
            EvaluateSensorSamples(measurement, status, sps30OffOnBattery, problems);
        }

        EvaluateEvents(snapshot.RecentEvents, measurement, TimeSpan.FromMinutes(intervalMinutes), options, now, issues, problems);

        List<StationIssue> ordered = issues
            .GroupBy(issue => issue.Key)
            .Select(group => group.OrderByDescending(issue => issue.Severity).First())
            .OrderByDescending(issue => issue.Severity)
            .ToList();

        return new StationEvaluation
        {
            Health = ordered.Count == 0 ? StationHealth.Ok : ordered[0].Severity,
            IsStale = stale,
            Issues = ordered,
            SensorProblems = problems
        };
    }

    /// <summary>
    /// Formatuje stari ve tvaru "35 min", "2 h 5 min", "3 d".
    /// </summary>
    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalMinutes < 1)
        {
            return "méně než 1 min";
        }

        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes} min";
        }

        if (age.TotalDays < 1)
        {
            return age.Minutes == 0 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalHours} h {age.Minutes} min";
        }

        return $"{(int)age.TotalDays} d";
    }

    /// <summary>
    /// Mistni cas; datum se pridava jen u starsich zaznamu.
    /// </summary>
    public static string FormatTime(DateTimeOffset value, DateTimeOffset now)
    {
        DateTime local = value.ToLocalTime().DateTime;
        return local.Date == now.ToLocalTime().Date
            ? local.ToString("HH:mm", Czech)
            : local.ToString("d. M. HH:mm", Czech);
    }

    /// <summary>
    /// Cas udalosti: cas stanice (OccurredAt), pokud je verohodny, jinak cas prijeti serverem.
    /// </summary>
    private static DateTimeOffset EventTime(DeviceEvent deviceEvent)
    {
        TimeSpan skew = deviceEvent.CreatedAt - deviceEvent.OccurredAt;
        return deviceEvent.OccurredAt != default && skew >= TimeSpan.FromMinutes(-5) && skew <= TimeSpan.FromDays(1)
            ? deviceEvent.OccurredAt
            : deviceEvent.CreatedAt;
    }

    private static bool IsResolvedByLaterMeasurement(string group, DeviceEvent deviceEvent, Measurement? measurement, TimeSpan interval)
    {
        if (measurement is null)
        {
            return false;
        }

        DateTimeOffset eventTime = EventTime(deviceEvent);
        DateTimeOffset intervalStart = measurement.MeasuredAt - interval;
        if (intervalStart < eventTime)
        {
            return false; // mereni (alespon zcasti) pochazi z doby pred udalosti
        }

        string status = FirmwareEnums.Normalize(measurement.Status, FirmwareEnums.MeasurementStatusNames);
        int expected = measurement.ExpectedSampleCount;
        bool Healthy(int ok, int errors) => errors == 0 && (expected <= 0 || ok * 100 >= expected * 80);

        return group switch
        {
            // Firmware nastavi BackupPowerActive, pokud na baterii bezel i jediny vzorek; na baterii navic vypina SPS30.
            "Power" or "Battery" => status is not ("BackupPowerActive" or "Sps30DisabledOnBattery")
                && measurement.Sps30BatterySkippedCount == 0
                && !string.Equals(measurement.PowerSource?.Trim(), "BAT", StringComparison.OrdinalIgnoreCase),
            "SPS30" => status != "Sps30ReadFailed" && Healthy(measurement.Sps30OkCount, measurement.Sps30ErrorCount),
            "SHT40" => status != "Sht40ReadFailed" && Healthy(measurement.Sht40OkCount, measurement.Sht40ErrorCount),
            "BMP280" => status != "Bmp280ReadFailed" && Healthy(measurement.Bmp280OkCount, measurement.Bmp280ErrorCount),
            // Mereni doslo na server po udalosti => Wi-Fi i spojeni se serverem funguji.
            "WiFi" or "Server" => measurement.CreatedAt > deviceEvent.CreatedAt,
            // Mereni ma verohodny cas => cas je synchronizovany.
            "Clock" => measurement.CreatedAt > deviceEvent.CreatedAt
                && (measurement.CreatedAt - measurement.MeasuredAt).Duration() <= TimeSpan.FromHours(1),
            _ => false
        };
    }

    private static bool IsSps30OffOnBattery(Measurement measurement, string status)
        => status == "Sps30DisabledOnBattery"
            || (measurement.Sps30BatterySkippedCount > 0 && measurement.Sps30OkCount == 0);

    private static void EvaluateStatus(string status, List<StationIssue> issues)
    {
        switch (status)
        {
            case "":
            case "Ok":
                return;
            case "InsufficientSamples":
                issues.Add(new("Status", StationHealth.Warning, "Interval měl nedostatek platných vzorků."));
                return;
            case "Sht40ReadFailed":
                issues.Add(new("Status", StationHealth.Error, "Chyba čtení SHT40 (teplota/vlhkost)."));
                return;
            case "Bmp280ReadFailed":
                issues.Add(new("Status", StationHealth.Error, "Chyba čtení BMP280 (tlak)."));
                return;
            case "Sps30ReadFailed":
                issues.Add(new("Status", StationHealth.Error, "Chyba čtení SPS30 – koncentrace částic nejsou spolehlivé."));
                return;
            case "BackupPowerActive":
                issues.Add(new("Status", StationHealth.Warning, "Interval proběhl na záložní baterii."));
                return;
            case "Sps30DisabledOnBattery":
                issues.Add(new("Status", StationHealth.Warning, "Běh na baterii – SPS30 je vypnutý, PM se neměří."));
                return;
            default:
                issues.Add(new("Status", StationHealth.Warning, $"Neznámý stav intervalu z firmwaru: {status}"));
                return;
        }
    }

    private static void EvaluateSps30(Measurement measurement, string status, bool offOnBattery, List<StationIssue> issues)
    {
        if (offOnBattery || status == "Sps30ReadFailed")
        {
            return;
        }

        bool allPmMissing = measurement.Pm1 is null && measurement.Pm25 is null && measurement.Pm4 is null && measurement.Pm10 is null;
        if (allPmMissing)
        {
            issues.Add(new("Sps30NoValues", StationHealth.Error, "SPS30 nevrátil žádné hodnoty PM."));
            return;
        }

        if (measurement.Sps30ErrorCount > 0)
        {
            issues.Add(new(
                "Sps30Errors",
                StationHealth.Warning,
                $"SPS30: {measurement.Sps30ErrorCount} chybných čtení v intervalu ({measurement.Sps30OkCount}/{measurement.ExpectedSampleCount} platných)."));
        }
    }

    private static void EvaluateSps30Range(Measurement measurement, List<StationIssue> issues, List<SensorProblem> problems)
    {
        (string Label, double? Value, double Maximum, string Unit)[] values =
        [
            ("PM1", measurement.Pm1, Sps30MassMaximum, "µg/m³"),
            ("PM2,5", measurement.Pm25, Sps30MassMaximum, "µg/m³"),
            ("PM4", measurement.Pm4, Sps30MassMaximum, "µg/m³"),
            ("PM10", measurement.Pm10, Sps30MassMaximum, "µg/m³"),
            ("počet PM0,5", measurement.NumberPm05, Sps30NumberMaximum, "#/cm³"),
            ("počet PM10", measurement.NumberPm10, Sps30NumberMaximum, "#/cm³")
        ];

        (string Label, double? Value, double Maximum, string Unit) bad = values
            .FirstOrDefault(item => item.Value is { } value && (value < 0 || value > item.Maximum || double.IsNaN(value)));
        if (bad.Label is null)
        {
            return;
        }

        issues.Add(new(
            "Sps30Range",
            StationHealth.Error,
            $"SPS30 hlásí hodnotu mimo měřicí rozsah senzoru ({bad.Label} = {bad.Value!.Value.ToString("0", Czech)} {bad.Unit}, rozsah 0–{bad.Maximum.ToString("0", Czech)}) – pravděpodobná porucha senzoru nebo chyba čtení, ne skutečné znečištění."));
        problems.Add(new("SPS30", StationHealth.Error, "mimo rozsah senzoru"));
    }

    private static void EvaluateSensorSamples(Measurement measurement, string status, bool sps30OffOnBattery, List<SensorProblem> problems)
    {
        AddSampleProblems(problems, "SHT40", status == "Sht40ReadFailed", measurement.Sht40OkCount, measurement.Sht40ErrorCount, measurement.ExpectedSampleCount);
        AddSampleProblems(problems, "BMP280", status == "Bmp280ReadFailed", measurement.Bmp280OkCount, measurement.Bmp280ErrorCount, measurement.ExpectedSampleCount);

        if (sps30OffOnBattery)
        {
            problems.Add(new("SPS30", StationHealth.Warning, "vypnuto na baterii"));
        }
        else
        {
            AddSampleProblems(problems, "SPS30", status == "Sps30ReadFailed", measurement.Sps30OkCount, measurement.Sps30ErrorCount, measurement.ExpectedSampleCount);
        }

        if (string.Equals(measurement.PowerSource?.Trim(), "BAT", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(new("Battery", StationHealth.Warning, "běh na baterii"));
        }
    }

    private static void AddSampleProblems(List<SensorProblem> problems, string sensor, bool readFailed, int okCount, int errorCount, int expectedCount)
    {
        if (readFailed)
        {
            problems.Add(new(sensor, StationHealth.Error, $"chyba čtení {sensor}"));
        }
        else if (expectedCount > 0 && okCount == 0)
        {
            problems.Add(new(sensor, StationHealth.Error, $"{sensor}: žádný platný vzorek"));
        }
        else if (errorCount > 0)
        {
            problems.Add(new(sensor, StationHealth.Warning, $"{errorCount}× chyba čtení"));
        }
    }

    private static void EvaluateFlags(IReadOnlyList<MeasurementQualityFlag> flags, List<StationIssue> issues, List<SensorProblem> problems)
    {
        foreach (MeasurementQualityFlag flag in flags)
        {
            int rank = FirmwareEnums.SeverityRank(flag.Severity);
            if (rank == 0)
            {
                continue; // Information, napr. BatteryContext.
            }

            string channel = flag.SensorChannel?.Code ?? flag.SensorChannelId.ToString(CultureInfo.InvariantCulture);
            string check = flag.QualityCheck?.Code ?? flag.QualityCheckId.ToString(CultureInfo.InvariantCulture);
            string message = check == "MissingValue" ? $"Chybí hodnota: {channel}." : flag.Message;
            StationHealth severity = rank >= 2 ? StationHealth.Error : StationHealth.Warning;
            issues.Add(new($"Flag:{check}:{channel}", severity, $"Kontrola kvality: {message}"));
            problems.Add(new(channel, severity, FirmwareEnums.QualityCheckShortTexts.GetValueOrDefault(check, check)));
        }
    }

    private static void EvaluateEvents(
        IReadOnlyList<DeviceEvent> events,
        Measurement? measurement,
        TimeSpan interval,
        MonitorOptions options,
        DateTimeOffset now,
        List<StationIssue> issues,
        List<SensorProblem> problems)
    {
        Dictionary<string, DeviceEvent> active = new(StringComparer.OrdinalIgnoreCase);
        DeviceEvent? lastRestart = null;

        foreach (DeviceEvent deviceEvent in events)
        {
            string code = FirmwareEnums.Normalize(deviceEvent.Code, FirmwareEnums.EventCodeNames);

            if (code is "DeviceStarted" or "DeviceRestarted")
            {
                // Po startu firmware znovu hlasi poruchy, ktere stale trvaji.
                active.Clear();
                lastRestart = deviceEvent;
                continue;
            }

            if (FirmwareEnums.RecoveryGroups.TryGetValue(code, out string[]? recovered))
            {
                foreach (string group in recovered)
                {
                    active.Remove(group);
                }

                continue;
            }

            if (deviceEvent.ResolvedAt is not null)
            {
                continue;
            }

            if (FirmwareEnums.FailureGroups.TryGetValue(code, out string? failureGroup))
            {
                active[failureGroup] = deviceEvent;
            }
            else if (FirmwareEnums.SeverityRank(deviceEvent.Severity) >= 2)
            {
                active["Event:" + code] = deviceEvent;
            }
        }

        TimeSpan timeLimit = TimeSpan.FromMinutes(options.TimeLimitedEventMinutes);
        foreach ((string group, DeviceEvent activeEvent) in active)
        {
            bool timeLimited = FirmwareEnums.TimeLimitedGroups.Contains(group) || group.StartsWith("Event:", StringComparison.Ordinal);
            if (timeLimited && now - activeEvent.CreatedAt > timeLimit)
            {
                continue;
            }

            // Udalost zotaveni se muze ztratit (firmware, upload). Rozhoduje proto i novejsi mereni:
            // pokud cely interval mereni zacal az po udalosti a komponenta v nem fungovala, porucha uz netrva.
            if (IsResolvedByLaterMeasurement(group, activeEvent, measurement, interval))
            {
                continue;
            }

            int rank = FirmwareEnums.SeverityRank(activeEvent.Severity);
            StationHealth severity = rank >= 2 ? StationHealth.Error : StationHealth.Warning;
            string label = FirmwareEnums.GroupLabels.GetValueOrDefault(group, group.Replace("Event:", string.Empty, StringComparison.Ordinal));
            string code = FirmwareEnums.Normalize(activeEvent.Code, FirmwareEnums.EventCodeNames);
            string detail = string.IsNullOrWhiteSpace(activeEvent.Message) ? code : activeEvent.Message.Trim();
            string since = FormatTime(activeEvent.CreatedAt, now);
            issues.Add(new($"Event:{group}", severity, $"{label}: {detail} (od {since})"));

            // Poruchy senzoru a napajeni se zobrazuji i primo u prislusnych ukazatelu.
            string? target = group switch
            {
                "SHT40" or "BMP280" or "SPS30" => group,
                "Battery" or "Power" => "Battery",
                _ => null
            };
            if (target is not null)
            {
                string shortText = FirmwareEnums.EventShortTexts.GetValueOrDefault(code, label);
                problems.Add(new(target, severity, $"{shortText} (od {since})"));
            }
        }

        if (lastRestart is not null && now - lastRestart.CreatedAt <= TimeSpan.FromMinutes(options.RestartWarningMinutes))
        {
            issues.Add(new("Restart", StationHealth.Warning, $"Stanice byla restartována v {FormatTime(lastRestart.CreatedAt, now)}."));
        }
    }
}
