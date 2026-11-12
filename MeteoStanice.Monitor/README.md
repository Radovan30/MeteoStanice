# MeteoStanice.Monitor

Desktopová aplikace (WPF, .NET 10), která **pouze čte** databázi PostgreSQL a zobrazuje aktuální koncentrace prachových částic ze všech aktivních stanic. Každá stanice má kompaktní kartu se dvěma řadami kulatých ukazatelů – prachové částice PM1, PM2,5, PM4 a PM10 (µg/m³; úroveň podle orientačních prahů ukazuje barevný štítek) a prostředí (teplota, relativní vlhkost, tlak přepočtený na hladinu moře) – a se stavem stanice. Chyby senzoru se zobrazují přímo u ukazatelů, které daný senzor měří (SHT40 → teplota a vlhkost, BMP280 → tlak, SPS30 → PM). Pokud stanice hlásí chybu nebo neměří, aplikace spustí zvukový alarm.

## Spuštění

1. Nastavte `ConnectionStrings:MeteoDatabase` v `appsettings.json` (nebo v `appsettings.Local.json`, případně v proměnné prostředí `ConnectionStrings__MeteoDatabase`).
2. Doporučení: pro monitor vytvořte v PostgreSQL samostatnou roli s právem pouze `SELECT`.
3. Spusťte projekt `MeteoStanice.Monitor` ze solution `MeteoStanice.Backend.sln`.

Aplikace nespouští migrace a nic do databáze nezapisuje. Předpokládá schéma, které vytvořilo API (`MeteoStanice.Api` aplikuje migrace při startu).

## Barvy a výběr stanic

Každá veličina má vlastní barvu oblouku a popisku; podklad oblouku je světlá varianta téže barvy. Úroveň koncentrace PM ukazuje barevný štítek s textem pod ukazatelem, takže význam nenese jen barva.

| Prvek | Barva | Hex |
|---|---|---|
| PM1 | fialová | `#8E24AA` |
| PM2,5 | indigová | `#3949AB` |
| PM4 | tyrkysová | `#00ACC1` |
| PM10 | hnědá | `#6D4C41` |
| Teplota | červená | `#D32F2F` |
| Vlhkost | modrá | `#1E88E5` |
| Tlak (moře) | zelená, dělený oblouk | `#2E7D32` |
| Stanice 1 / 2 / 3 | světle modrá / světle oranžová / růžová | `#4A90D9` / `#EF8B3B` / `#D2609A` |
| Štítek úrovně PM | zelená nízká / oranžová zvýšená / červená vysoká | `#2E7D32` / `#E09A00` / `#C62828` |

Stanice mají barevný proužek, záhlaví a název (pořadí podle kódu stanice). Stav stanice (V POŘÁDKU / VAROVÁNÍ / CHYBA / NEMĚŘÍ) zůstává ve štítku vpravo nahoře. Barvy se mění v `appsettings.json` (`Color` u veličin, `StationColors`).

V liště **Zobrazit** lze kliknutím zobrazit nebo skrýt jednotlivé stanice, dvojklikem zobrazit jen jednu a tlačítkem „Vše“ všechny. Okno se šířkou přizpůsobí počtu zobrazených karet (1–3 vedle sebe) a výškou obsahu. Tečka u názvu stanice ukazuje její stav i tehdy, když je karta skrytá. **Alarm hlídá všechny stanice, i skryté.**

## Režim widgetu (připnutí na plochu)

Okno nemá systémový rámeček (vlastní lišta s tlačítky — minimalizovat a ✕ zavřít) a je trvale průhledné (`AllowsTransparency`), protože průhlednost nelze ve WPF zapnout až za běhu. Tlačítko 📌 přepne okno do režimu widgetu: **pozadí je zcela průhledné** (na ploše zůstane jen poloprůhledná lišta a karty stanic, přes průhledná místa lze klikat na plochu), nadpis se skryje a okno je připnuté na ploše Windows, drží se **za ostatními okny**, není na hlavním panelu ani v Alt+Tab a zůstává viditelné i po Win+D. Přesouvá se tažením za horní lištu (i v normálním režimu) a zavírá tlačítkem ✕. Opětovným kliknutím na 📌 se okno vrátí do normálního režimu.

Technicky se vlastníkem okna stane okno plochy (`Progman`), okno dostane styl `WS_EX_TOOLWINDOW` a zpráva `WM_WINDOWPOSCHANGING` vrací okno vždy na spodek pořadí oken (`HWND_BOTTOM`), viz `Services/DesktopPinning.cs`.

Poloha okna, režim widgetu a skryté stanice se ukládají do `%LOCALAPPDATA%\MeteoStanice.Monitor\ui-state.json` a obnoví se při dalším spuštění.

## Obnovování dat

Aplikace se databáze periodicky nedotazuje. Využívá mechanismus PostgreSQL **LISTEN/NOTIFY** přes knihovnu Npgsql (obdoba `SqlDependency` u SQL Serveru, která s PostgreSQL nefunguje):

1. Jednorázově spusťte v DB `Database/monitor-notify.sql`. Skript přidá jen funkci a statement-level triggery volající `pg_notify`; data, tabulky ani EF migrace nemění. Kanál v triggeru (`meteo_monitor_changed`) musí odpovídat `NotifyChannel`.
2. Monitor drží jedno spojení s `LISTEN meteo_monitor_changed`. Po každém INSERT/UPDATE/DELETE v tabulkách `Measurements`, `DeviceEvents`, `MeasurementQualityFlags`, `Stations` a `StationSettings` PostgreSQL pošle notifikaci a monitor data okamžitě načte. Změny těsně po sobě (měření, příznaky kvality, `LastSeenAt`) se sloučí do jednoho načtení (500 ms).
3. Každých `ReevaluateIntervalSeconds` (30 s) se **bez dotazu do DB** přepočítá stáří již načtených dat, protože výpadek stanice se projeví plynutím času, ne změnou v databázi.
4. Pokud trigger chybí nebo je spojení přerušené, horní lišta to oznámí a do obnovení notifikací běží záložní načítání každých `FallbackRefreshSeconds` (60 s). Spojení se obnovuje automaticky a po obnovení se data načtou znovu.
5. Spojení pro notifikace se každou minutu ověří (žije spojení, existuje trigger) – mrtvé spojení se obnoví, chybějící trigger zapne záložní načítání. Navíc pojistka: úplné načtení každých `SafetyRefreshMinutes` (5 min) a po kliknutí na „Potvrdit alarm“.

### Ošetření chybných záznamů

- Firmware posílá při nulovém počtu platných vzorků hodnotu `0` místo „chybí“. Monitor proto hodnoty senzoru s `…OkCount = 0` (SPS30 → PM, SHT40 → teplota/vlhkost, BMP280 → tlak) zobrazuje jako chybějící, ne jako naměřenou nulu. Data v DB se nemění.
- Hodnoty SPS30 mimo měřicí rozsah senzoru (hmotnostní koncentrace 0–1000 µg/m³, početní 0–3000 #/cm³ dle datasheetu Sensirion) se nevyhodnocují jako „vysoká“ koncentrace, ale jako **CHYBA senzoru** (šedé ukazatele PM, alarm poruchy, ne alarm znečištění).
- Za poslední měření se bere záznam s nejvyšším `MeasuredAt`, ale záznamy s časem více než 10 min v budoucnosti (chybný čas stanice) se ignorují a na kartě se zobrazí varování – jinak by takový záznam „zamrazil“ kartu.

## Kulaté ukazatele

| Veličina | Stupnice | Vzhled | Upozornění | Chyby senzoru |
|---|---|---|---|---|
| Teplota | −20 až 50 °C | červený plynulý oblouk | – | SHT40 |
| Relativní vlhkost | 0–100 % | modrý plynulý oblouk | nad 85 % (SPS30 může při vysoké RH nadhodnocovat PM) | SHT40 |
| Tlak přepočtený na hladinu moře | 960–1050 hPa | zelený dělený oblouk (15 dílků), pod ním tlak stanice a výška | – | BMP280 |

Pod ukazatelem je rozsah min–max za interval měření. Chyba senzoru (stav intervalu, nulový počet platných vzorků, chybná čtení, aktivní poruchová událost, příznak kvality pro daný kanál) obarví oblouk šedě (chyba – hodnota nemusí být platná) nebo oranžově (varování) a vypíše krátký důvod (✖ chyba, ⚠ varování). Při chybě SPS30 se ukazatele PM zobrazí šedě a hodnota nespouští alarm znečištění, protože nemusí být platná. Stupnice, prahy, barva (`Color`) a dělení oblouku (`Segments`, 0 = plynulý) jsou nastavitelné v `EnvironmentGauges`.

## Přepočet tlaku na hladinu moře

BMP280 měří tlak v místě stanice (staniční tlak). Ten se pro srovnání s předpovědí a s ČHMÚ přepočítává na hladinu moře hypsometrickou rovnicí:

p₀ = p · exp(g·h / (R_d · T_m)),  T_m = T + 273,15 + 0,0065·h/2

kde g = 9,80665 m·s⁻², R_d = 287,05 J·kg⁻¹·K⁻¹ a T je naměřená teplota stanice. Pokud je čidlo teploty v interiéru, nastavte `UseMeasuredTemperature: false`, aby se použila standardní teplota 15 °C. Výška se hledá v `PressureReduction:StationAltitudes` podle `StationCode` nebo `LocationText`, jinak se použije `DefaultAltitudeMeters` (242 m, výška měřicí stanice ČHMÚ Ostrava-Poruba). Jde o zjednodušenou redukci bez vlivu vlhkosti, proto se od oficiálních hodnot ČHMÚ může lišit o desetiny hPa. Přesnost závisí hlavně na zadané výšce čidla: 1 m výšky odpovídá přibližně 0,12 hPa.

## Zdroje dat

| Tabulka | Použití |
|---|---|
| `Stations` | aktivní stanice (`IsActive`), název, kód, umístění, `LastSeenAt` |
| `StationSettings` | interval měření (výchozí 10 min) |
| `Measurements` | poslední záznam podle `MeasuredAt` – PM, stav intervalu, počty vzorků, napájení |
| `MeasurementQualityFlags` | příznaky kvality posledního měření (bez úrovně `Information`) |
| `DeviceEvents` | události za posledních 24 h (podle `CreatedAt`) |

## Vyhodnocení stavu stanice

| Stav | Podmínka |
|---|---|
| **NEMĚŘÍ** | žádné měření nebo poslední `MeasuredAt` starší než `interval × OfflineAfterIntervals` (výchozí 10 min × 2,5 = 25 min). Rozlišuje se, zda stanice ještě komunikuje (`LastSeenAt`). |
| **CHYBA** | stav intervalu `Sht40ReadFailed`, `Bmp280ReadFailed`, `Sps30ReadFailed`; všechny PM hodnoty chybí; aktivní poruchová událost se závažností Error/Critical |
| **VAROVÁNÍ** | `InsufficientSamples`, běh na baterii, SPS30 vypnutý na baterii, chybná čtení SPS30, příznaky kvality, nedávný restart, čas měření v budoucnosti |

Poruchová událost je aktivní, dokud nepřijde párová událost zotavení (např. `Sps30ReadFailed` → `Sps30Restored`, `WifiConnectionFailed` → `WifiConnected`), restart stanice (`DeviceStarted`/`DeviceRestarted`) **nebo novější měření, jehož celý interval začal až po události a ve kterém daná komponenta fungovala**. Napájení: bez stavu `BackupPowerActive`/`Sps30DisabledOnBattery` a bez vynechaných vzorků SPS30 kvůli baterii. SPS30/SHT40/BMP280: bez chyb čtení a ≥ 80 % platných vzorků. Wi-Fi/server: měření dorazilo na server po události. Čas: měření má věrohodný čas. Důvod: událost zotavení se může ztratit, například když firmware po restartu čísluje události znovu od 1 a API je odmítne jako duplicitní. Události bez páru (lokální fronta, neznámé kódy) se zobrazují `TimeLimitedEventMinutes` (60 min). Číselné hodnoty výčtů z nanoFrameworku se převádějí podle `Models/FirmwareEnums.cs` – **pořadí musí odpovídat firmwaru**.

## Alarm

Alarm vzniká pro stavy CHYBA a NEMĚŘÍ, pro nedostupnou databázi a volitelně (`AlarmOnHighPollution`) pro koncentraci nad alarmovým prahem. Nový alarm se ozve okamžitě a opakuje se každých `RepeatSeconds`, dokud jej uživatel nepotvrdí. Potvrzení ztlumí jen aktuální alarmy – nový problém nebo opětovný výskyt se ozve znovu. Zvuk je `Sounds/alarm.wav`; když soubor chybí, použije se systémový zvuk Windows.

## Prahy koncentrací

Barvy a prahy ukazatelů PM jsou **orientační** a nastavitelné v `appsettings.json`. Výchozí prahy „zvýšená“ pro PM2,5 (15 µg/m³) a PM10 (45 µg/m³) odpovídají 24hodinovým doporučeným hodnotám WHO 2021. Zobrazená hodnota je však 10minutový průměr z nekalibrovaného nízkonákladového senzoru, proto ji nelze přímo srovnávat s 24hodinovými limity. Prahy pro PM1 a PM4 nemají oficiální předlohu a jsou zvoleny pouze pro vizualizaci.
