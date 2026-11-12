<#
.SYNOPSIS
  Sestavi instalator MeteoStanice Monitor.

.DESCRIPTION
  1. dotnet publish s profilem FolderProfile (Release, win-x64, self-contained, jeden .exe)
  2. kompilace Inno Setup skriptu MeteoStanice.Monitor.iss
  Vysledek: MeteoStanice.Monitor\bin\Installer\MeteoStanice.Monitor-Setup-<verze>.exe

  Predpoklad: Inno Setup 6  ->  winget install JRSoftware.InnoSetup

.EXAMPLE
  .\build-installer.ps1
  .\build-installer.ps1 -Version 1.1.0
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectDir "MeteoStanice.Monitor.csproj"

Write-Host "1/2 Publikace aplikace (Release, win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish $project -p:PublishProfile=FolderProfile -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish selhal." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 neni nainstalovany. Nainstalujte: winget install JRSoftware.InnoSetup"
}

Write-Host "2/2 Kompilace instalatoru ($iscc)..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "MeteoStanice.Monitor.iss")
if ($LASTEXITCODE -ne 0) { throw "Kompilace instalatoru selhala." }

$output = Join-Path $projectDir "bin\Installer\MeteoStanice.Monitor-Setup-$Version.exe"
Write-Host "Hotovo: $output" -ForegroundColor Green
