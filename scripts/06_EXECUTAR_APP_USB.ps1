# APENAS LABORATORIO: adb reverse torna o localhost do notebook acessivel ao Android USB.
param([string]$DispositivoId = '')
$ErrorActionPreference='Stop'
$base = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) { throw 'Instale Flutter SDK e Android SDK.' }
$adbCmd = Get-Command adb -ErrorAction SilentlyContinue
$adb = if ($adbCmd) { $adbCmd.Source } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe' }
if (-not (Test-Path $adb)) { throw 'adb.exe nao encontrado. Instale Android SDK Platform-Tools.' }
try { $status = Invoke-RestMethod 'http://localhost:5102/healthz' -TimeoutSec 5 }
catch { throw 'API local nao responde em localhost:5102. Inicie primeiro 03_INICIAR_API_LOCAL.ps1.' }
if ($status.status -ne 'ok') { throw 'API /healthz nao respondeu ok.' }
$dispositivos = @(& $adb devices | Where-Object { $_ -match '^([^\s]+)\s+device\s*$' } | ForEach-Object { ($_ -split '\s+')[0] })
if ($DispositivoId -eq '') {
  if ($dispositivos.Count -eq 0) { throw 'Nenhum celular autorizado. Habilite Depuracao USB e aceite a autorizacao na tela do Android.' }
  if ($dispositivos.Count -gt 1) { throw "Ha mais de um Android. Use -DispositivoId ID. Opcoes: $($dispositivos -join ', ')" }
  $DispositivoId = $dispositivos[0]
} elseif ($DispositivoId -notin $dispositivos) { throw "Android $DispositivoId nao encontrado/autorizado." }
& $adb -s $DispositivoId reverse tcp:5102 tcp:5102
if ($LASTEXITCODE -ne 0) { throw 'adb reverse falhou. Confira cabo USB e Depuracao USB.' }
& $adb -s $DispositivoId reverse --list
Write-Host 'Canal USB ativo! O IP 127.0.0.1:5102 no APP acessa a API do NOTEBOOK.' -ForegroundColor Green
Write-Host 'Mesmo em 4G/5G, o encaminhamento USB funciona com o cabo conectado.'
Push-Location (Join-Path $base 'app_flutter')
try {
  & flutter pub get; if ($LASTEXITCODE -ne 0) { throw 'flutter pub get falhou.' }
  & flutter analyze --no-fatal-infos; if ($LASTEXITCODE -ne 0) { throw 'flutter analyze apontou erros.' }
  & flutter test; if ($LASTEXITCODE -ne 0) { throw 'flutter test falhou.' }
  & flutter run -d $DispositivoId '--dart-define=MERKDITO_API_URL=http://127.0.0.1:5102'
  if ($LASTEXITCODE -ne 0) { throw 'flutter run falhou.' }
} finally { Pop-Location }
