#Requires -Version 5.1
param(
  [string]$Version = "0.3.0",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publishDir = Join-Path $root "dist\win-x64"
$portableZip = Join-Path $root "dist\SpiritVale-Overlay-v$Version-win-x64.zip"
$setupExe = Join-Path $root ("dist\SpiritVale Plugin Manager_{0}_x64-setup.exe" -f $Version)

Write-Host "==> Publishing host to $publishDir"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish src\SpiritVale.Overlay.Host\SpiritVale.Overlay.Host.csproj `
  -c $Configuration -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$pluginsDir = Join-Path $publishDir "Plugins"
if (Test-Path $pluginsDir) { Remove-Item -Recurse -Force $pluginsDir }
New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

Get-ChildItem -Recurse $publishDir -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "==> Portable zip $portableZip"
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portableZip -CompressionLevel Optimal

$makensisCmd = Get-Command makensis -ErrorAction SilentlyContinue
$makensisPath = $null
if ($makensisCmd) {
  $makensisPath = $makensisCmd.Source
} else {
  $candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"),
    (Join-Path $env:ProgramFiles "NSIS\makensis.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\NSIS\makensis.exe")
  )
  foreach ($c in $candidates) {
    if (Test-Path $c) { $makensisPath = $c; break }
  }
}

if (-not $makensisPath) {
  Write-Warning "makensis not found - portable zip only. Install NSIS to build the setup.exe."
} else {
  Write-Host "==> NSIS $makensisPath"
  & $makensisPath ("/DVERSION={0}" -f $Version) ("/DPUBLISHDIR={0}" -f $publishDir) (Join-Path $root "tools\plugin-manager.nsi")
  if ($LASTEXITCODE -ne 0) { throw "makensis failed" }
  if (-not (Test-Path $setupExe)) { throw "Expected setup not found: $setupExe" }
}

Write-Host "Done."
Write-Host "  Portable: $portableZip"
if (Test-Path $setupExe) { Write-Host "  Setup:    $setupExe" }
