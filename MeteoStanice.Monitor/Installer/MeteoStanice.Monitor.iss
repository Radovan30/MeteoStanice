; =====================================================================
; Instalator MeteoStanice Monitor (Inno Setup 6)
; ---------------------------------------------------------------------
; Postup: spustte build-installer.ps1 (publikuje aplikaci a zkompiluje
; tento skript). Vysledek: ..\bin\Installer\MeteoStanice.Monitor-Setup-<verze>.exe
;
; Instalace pro aktualniho uzivatele (bez administratorskych prav) do
; %LOCALAPPDATA%\Programs\MeteoStanice Monitor – appsettings.json tak
; zustava upravitelny bez prav spravce.
; =====================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName      "MeteoStanice Monitor"
#define AppExe       "MeteoStanice.Monitor.exe"
#define AppPublisher "MeteoStanice"
#define PublishDir   "..\bin\Release\publish\win-x64"

[Setup]
; AppId identifikuje aplikaci pri aktualizaci a odinstalaci – NEMENIT.
AppId={{837D6E52-8FFA-470A-9EF7-65A261773613}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\bin\Installer
OutputBaseFilename=MeteoStanice.Monitor-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Pri aktualizaci zavrit bezici monitor (jinak nelze prepsat .exe).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Spouštět automaticky po přihlášení do Windows (např. jako widget na ploše)"; GroupDescription: "Další možnosti:"

[Files]
; Aplikace (jeden soubor, .NET je pribaleny – self-contained publish).
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; Konfigurace: pri aktualizaci se NEPREPISE, aby zustalo nastaveni pripojeni k DB, vysky a barev.
Source: "{#PublishDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\Sounds\*"; DestDir: "{app}\Sounds"; Flags: ignoreversion recursesubdirs
; Pripadne dalsi soubory z publikace (nativni knihovny apod.).
Source: "{#PublishDir}\*"; Excludes: "{#AppExe},appsettings.json,appsettings.Local.json,Sounds\*,*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist
; SQL skript s triggery pro okamzite obnovovani (spousti se rucne v DB).
Source: "..\Database\monitor-notify.sql"; DestDir: "{app}\Database"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autoprograms}\{#AppName} – nastavení (appsettings.json)"; Filename: "notepad.exe"; Parameters: """{app}\appsettings.json"""
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Ulozena poloha okna a vyber stanic (%LOCALAPPDATA%\MeteoStanice.Monitor).
Type: filesandordirs; Name: "{localappdata}\MeteoStanice.Monitor"
