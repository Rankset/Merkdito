# Merkdito PIM - ambiente local Windows PowerShell 5.1+ (somente leitura)
$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
Write-Host "=== MERKDITO | DIAGNOSTICO LOCAL ===" -ForegroundColor Cyan
Write-Host "Pasta do projeto: $base"
$comandos = @('dotnet','flutter','git')
foreach ($cmd in $comandos) {
  $c = Get-Command $cmd -ErrorAction SilentlyContinue
  if ($null -eq $c) { Write-Host "PENDENTE: $cmd" -ForegroundColor Yellow }
  else { Write-Host "OK: $cmd -> $($c.Source)" -ForegroundColor Green }
}
$adb = Get-Command adb -ErrorAction SilentlyContinue
if ($null -eq $adb) {
  $android = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
  if (Test-Path $android) { $adb = @{Source=$android} }
}
if ($null -eq $adb) { Write-Host 'PENDENTE: adb (Android SDK Platform-Tools)' -ForegroundColor Yellow }
else {
  Write-Host "OK: adb -> $($adb.Source)" -ForegroundColor Green
  & $adb.Source devices
}
Get-Service -Name 'MSSQL*' -ErrorAction SilentlyContinue | Format-Table Status,Name,DisplayName
foreach ($p in @('Merkdito.sln','Merkdito.Api/Merkdito.Api.csproj','app_flutter/pubspec.yaml','sql/00_MerkditoDb_original.sql')) {
  if (Test-Path (Join-Path $base $p)) { Write-Host "OK: $p" -ForegroundColor Green }
  else { Write-Warning "Arquivo ausente: $p" }
}
Write-Host 'Se faltar SQL Server, instale SQL Server Express + SSMS. Nao execute scripts no banco de producao.'
