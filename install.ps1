# install.ps1 — publikuje PhotoManager i wgrywa do stabilnej lokalizacji użytkownika,
# tworzy skróty (Start Menu + Pulpit) oraz wpis autostartu. Bez uprawnień administratora.
#
# Użycie:
#   pwsh -File install.ps1            # pełna instalacja/aktualizacja
#   pwsh -File install.ps1 -NoLaunch  # bez uruchamiania na końcu

param([switch]$NoLaunch)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\PhotoManager.App\PhotoManager.App.csproj"
$distDir = Join-Path $root "dist"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\PhotoManager"
$exe = Join-Path $installDir "PhotoManager.App.exe"

# 0) Zatrzymaj działającą instancję (żeby nie blokowała plików).
Get-Process -Name "PhotoManager.App" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# 1) Publikacja (framework-dependent — korzysta z zainstalowanego .NET).
Write-Host "Publikuję..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o $distDir | Out-Null

# 2) Wgranie do stabilnej lokalizacji.
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $distDir "*") -Destination $installDir -Recurse -Force
Write-Host "Zainstalowano: $installDir" -ForegroundColor Green

# 3) Skróty (idempotentnie).
$sh = New-Object -ComObject WScript.Shell
function New-Shortcut($path) {
    $lnk = $sh.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $installDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = "PhotoManager — import zdjęć z aparatu"
    $lnk.Save()
}
New-Shortcut (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PhotoManager.lnk")
New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) "PhotoManager.lnk")

# 4) Autostart.
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "PhotoManager" -Value "`"$exe`""
Write-Host "Skróty i autostart gotowe." -ForegroundColor Green

# 5) Uruchom.
if (-not $NoLaunch) {
    Start-Process -FilePath $exe
    Write-Host "Uruchomiono PhotoManager." -ForegroundColor Green
}
