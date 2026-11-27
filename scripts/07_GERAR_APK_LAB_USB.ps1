# APK para demonstração local USB, NAO comercial; valida a API antes.
$ErrorActionPreference='Stop'
$base = Split-Path -Parent $PSScriptRoot
try { $saude=Invoke-RestMethod 'http://localhost:5102/healthz' -TimeoutSec 5 }
catch { throw 'API local nao esta funcionando. Valide a integracao antes de gerar o APK de laboratorio.' }
if ($saude.status -ne 'ok') { throw 'healthz nao confirmou API.' }
& (Join-Path $PSScriptRoot 'GERAR_APK_ANDROID.ps1') -ApiUrl 'http://127.0.0.1:5102'
if ($LASTEXITCODE -ne 0) { throw 'Geracao de APK falhou.' }
Write-Host 'ATENCAO: Para acessar a API com o APK por USB, execute adb reverse tcp:5102 tcp:5102 antes de abrir o app.' -ForegroundColor Yellow
