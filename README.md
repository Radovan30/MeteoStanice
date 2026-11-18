# MeteoStanice

Systém nízkonákladových stanic kvality ovzduší pro diplomovou práci *Automatická detekce anomálií v časových řadách z nízkonákladových stanic kvality ovzduší*. Stanice ESP32 (LILYGO TTGO T-Display) měří každých 10 s veličiny ze senzorů SHT40 (teplota, vlhkost), BMP280 (tlak) a SPS30 (PM1–PM10, početní koncentrace). Surové vzorky posílají přes Wi-Fi do ASP.NET Core API, které je ukládá do PostgreSQL, počítá desetiminutové agregáty a pravidlovou kontrolu kvality.

Podrobný technický popis (architektura, protokol, databáze, kontrola kvality, omezení a plán) je v [`docs/ARCHITEKTURA_A_POPIS_SYSTEMU.md`](docs/ARCHITEKTURA_A_POPIS_SYSTEMU.md).

## Struktura repozitáře

| Složka / soubor | Obsah |
|---|---|
| `MeteoStanice/` | Firmware stanice (C#, .NET nanoFramework), solution `MeteoStanice.slnx` |
| `MeteoStanice.Api/` | REST API, registrace stanic, backendová agregace, SignalR, Swagger |
| `MeteoStanice.Data/` | Entity Framework Core model a migrace PostgreSQL |
| `MeteoStanice.Contracts/` | Přenosové objekty a sdílené číselníky stavů (firmware ↔ API ↔ Monitor) |
| `MeteoStanice.Quality/` | Pravidlová kontrola kvality desetiminutových agregátů |
| `MeteoStanice.Monitor/` | WPF aplikace pro sledování stanic a alarmy ([README](MeteoStanice.Monitor/README.md)) |
| `MeteoStanice.Web/` | Připravená kostra webového dashboardu (zatím bez funkcí) |
| `MeteoStanice.Backend.sln` | Solution pro API, knihovny, Monitor a Web |
| `docker-compose.yml` | Lokální PostgreSQL 17 + API |
| `docs/` | Dokumentace systému |

## Rychlé spuštění backendu

1. Docker: `docker compose up -d --build` spustí PostgreSQL (port 5432) a API (`http://localhost:5080`, Swagger na `/swagger`). Hesla a klíče v compose souboru jsou jen vývojová.
2. Bez Dockeru: vytvořte databázi `meteostanice` a soubor `MeteoStanice.Api/appsettings.Local.json` (není ve verzování):

   ```json
   {
     "ConnectionStrings": {
       "MeteoDatabase": "Host=localhost;Port=5432;Database=meteostanice;Username=<USER>;Password=<PASSWORD>"
     },
     "StationApi": { "ApiKey": "<KLIC_STANIC>" },
     "AdminApi": { "ApiKey": "<KLIC_ADMINISTRATORA>" }
   }
   ```

   Potom spusťte `MeteoStanice.Api`. API při startu samo aplikuje migrace.
3. Monitor: vytvořte `MeteoStanice.Monitor/appsettings.Local.json` s connection stringem a jednorázově spusťte v databázi `MeteoStanice.Monitor/Database/monitor-notify.sql`.

Tajné hodnoty (hesla, klíče) patří do `appsettings.Local.json` nebo do proměnných prostředí, nikdy ne do verzovaných `appsettings*.json`.

## Firmware

- Otevřete `MeteoStanice.slnx` ve Visual Studiu s rozšířením .NET nanoFramework; cílová deska `ESP32_GenericDisplay_REV0`.
- První nastavení: stiskněte tlačítko na GPIO35 za běhu stanice, připojte se k Wi-Fi `MeteoStanice-TTGO-Setup` (výchozí heslo `12345678`, doporučeno změnit) a otevřete `http://192.168.4.1/`. Zadejte Wi-Fi, URL API (např. `http://192.168.0.102:32778/api`), API klíč a místo stanice.
- Stanice ukládá vzorky až po synchronizaci času z NTP (na displeji do té doby `T--`). Bez Wi-Fi tedy nevznikají data s neplatným časem.
- Před aktualizací firmwaru z 0.3.x na 0.4.0 nechte vyprázdnit frontu (`Q00` na displeji). Formát flash fronty se změnil a nevyprázdněná fronta se při startu založí znovu.

## Data pro analýzu

- Surové 10s vzorky: `GET /api/samples/export.csv?stationGuid=…&from=…&to=…` (UTC, invariantní CSV).
- Desetiminutové agregáty: `GET /api/measurements/{stationGuid}/recent?take=…`, případně přímo tabulka `Measurements`.
- Události stanic (výpadky napájení a Wi-Fi, poruchy senzorů, setup režim): tabulka `DeviceEvents` nebo `GET /api/events/{stationGuid}/recent`.
- Diagnostika přenosu (duplicity, odmítnuté vzorky, zpoždění, ztráty ve frontách stanice, restarty): `GET /api/samples/upload-batches.csv`; u vzorků sloupec `ReceiveCount`.
- Deník zásahů a referenční anotace (souběžné měření, přesuny, čištění, vyvolané anomálie, skutečné události): `GET /api/annotations/export.csv`. Zápis přes `POST /api/annotations` s hlavičkou `X-Admin-Key`, příklady jsou v `MeteoStanice.Api/MeteoStanice.Api.http`. Postup vedení deníku popisuje kap. 23.0 dokumentace.

Raw hodnoty se nikdy nepřepisují. Korekce SPS30 se počítá jen ve výstupu API (`Corrected*`) a Monitoru.

## Stav

Stav implementace, plánované rozšíření (analytická část s detekcí anomálií, porovnání stanic, kalibrace, dashboard) a známá omezení jsou popsány v kapitolách 21–26 dokumentace. Změny firmwaru 0.4.0 je ještě potřeba ověřit na hardwaru (kap. 25, bod 1).
