-- =====================================================================
-- MeteoStanice.Monitor – upozorneni na zmeny v DB (LISTEN/NOTIFY)
-- =====================================================================
-- Monitor se databaze periodicky nedotazuje: data nacte az po notifikaci,
-- kterou tyto triggery poslou po kazdem INSERT/UPDATE/DELETE v tabulkach,
-- ze kterych monitor cte. Bez skriptu bezi jen zalozni nacitani (60 s).
--
-- Skript NEMENI data ani strukturu tabulek, pouze pridava funkci
-- a statement-level triggery, ktere volaji pg_notify. EF Core model
-- ani migrace to neovlivni. Spustit jednou (psql / pgAdmin) uzivatelem,
-- ktery je vlastnikem tabulek.
--
-- Odinstalace: viz konec souboru.
-- =====================================================================

CREATE OR REPLACE FUNCTION meteo_monitor_notify()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify('meteo_monitor_changed', TG_TABLE_NAME);
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS meteo_monitor_notify ON "Measurements";
CREATE TRIGGER meteo_monitor_notify
    AFTER INSERT OR UPDATE OR DELETE ON "Measurements"
    FOR EACH STATEMENT EXECUTE FUNCTION meteo_monitor_notify();

DROP TRIGGER IF EXISTS meteo_monitor_notify ON "DeviceEvents";
CREATE TRIGGER meteo_monitor_notify
    AFTER INSERT OR UPDATE OR DELETE ON "DeviceEvents"
    FOR EACH STATEMENT EXECUTE FUNCTION meteo_monitor_notify();

DROP TRIGGER IF EXISTS meteo_monitor_notify ON "MeasurementQualityFlags";
CREATE TRIGGER meteo_monitor_notify
    AFTER INSERT OR UPDATE OR DELETE ON "MeasurementQualityFlags"
    FOR EACH STATEMENT EXECUTE FUNCTION meteo_monitor_notify();

-- Stations: LastSeenAt se meni pri kazdem uploadu (co 10 s), proto trigger reaguje jen na zmeny
-- sloupcu, ktere monitor zobrazuje (EF Core v UPDATE nastavuje jen skutecne zmenene sloupce).
DROP TRIGGER IF EXISTS meteo_monitor_notify ON "Stations";
CREATE TRIGGER meteo_monitor_notify
    AFTER INSERT OR DELETE OR UPDATE OF "StationCode", "Name", "LocationText", "IsActive", "FirmwareVersion", "Sps30CorrectionPercent"
    ON "Stations"
    FOR EACH STATEMENT EXECUTE FUNCTION meteo_monitor_notify();

-- SensorSamples: nove 10s vzorky; monitor podle payloadu "SensorSamples" nacte jen posledni vzorky.
DROP TRIGGER IF EXISTS meteo_monitor_notify ON "SensorSamples";
CREATE TRIGGER meteo_monitor_notify
    AFTER INSERT ON "SensorSamples"
    FOR EACH STATEMENT EXECUTE FUNCTION meteo_monitor_notify();

DROP TRIGGER IF EXISTS meteo_monitor_notify ON "StationSettings";
CREATE TRIGGER meteo_monitor_notify
    AFTER INSERT OR UPDATE OR DELETE ON "StationSettings"
    FOR EACH STATEMENT EXECUTE FUNCTION meteo_monitor_notify();

-- ---------------------------------------------------------------------
-- Odinstalace:
-- DROP TRIGGER IF EXISTS meteo_monitor_notify ON "Measurements";
-- DROP TRIGGER IF EXISTS meteo_monitor_notify ON "DeviceEvents";
-- DROP TRIGGER IF EXISTS meteo_monitor_notify ON "MeasurementQualityFlags";
-- DROP TRIGGER IF EXISTS meteo_monitor_notify ON "Stations";
-- DROP TRIGGER IF EXISTS meteo_monitor_notify ON "StationSettings";
-- DROP TRIGGER IF EXISTS meteo_monitor_notify ON "SensorSamples";
-- DROP FUNCTION IF EXISTS meteo_monitor_notify();
-- ---------------------------------------------------------------------
