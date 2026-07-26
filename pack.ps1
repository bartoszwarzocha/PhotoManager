# pack.ps1 — buduje samodzielny (self-contained) exe i pakuje go do ZIP-a do wydania.
#
# Użycie:
#   pwsh -File pack.ps1 -Version 1.0.0

param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\PhotoManager.App\PhotoManager.App.csproj"
$out = Join-Path $root "publish"

Write-Host "Publikuję self-contained win-x64 (v$Version)…" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false `
  -p:Version=$Version `
  -o $out | Out-Null

$zip = Join-Path $root "PhotoManager-v$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out "PhotoManager.App.exe") -DestinationPath $zip

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Gotowe: $zip ($size MB)" -ForegroundColor Green
Write-Host "Wydanie na GitHub:  gh release create v$Version `"$zip`" --generate-notes" -ForegroundColor Gray
