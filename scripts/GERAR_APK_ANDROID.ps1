param([string]$ApiUrl = 'http://127.0.0.1:5102')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'app_flutter'
if (-not (Test-Path (Join-Path $app 'pubspec.yaml'))) { throw "Projeto Flutter não encontrado em $app" }
if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) { throw 'Instale e configure Flutter SDK e Android SDK antes de executar.' }
Push-Location $app
try {
    flutter pub get
    if ($LASTEXITCODE -ne 0) { throw 'flutter pub get falhou' }
    flutter analyze --no-fatal-infos
    if ($LASTEXITCODE -ne 0) { throw 'flutter analyze encontrou erros; APK não gerado.' }
    flutter test
    if ($LASTEXITCODE -ne 0) { throw 'flutter test falhou; APK não gerado.' }
    flutter build apk --debug "--dart-define=MERKDITO_API_URL=$ApiUrl"
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação do APK.' }
    $apk = Join-Path $app 'build\app\outputs\flutter-apk\app-debug.apk'
    if (-not (Test-Path $apk)) { throw 'A compilação terminou sem encontrar o APK.' }
    $destino = Join-Path $root 'MERKDITO_ANDROID_DEBUG.apk'
    Copy-Item -LiteralPath $apk -Destination $destino -Force
    Write-Host "APK gerado em: $destino" -ForegroundColor Green
    Write-Host 'Para usar a API local por USB: adb reverse tcp:5102 tcp:5102' -ForegroundColor Yellow
    Write-Host 'Este APK é DEBUG e não representa homologação.'
} finally { Pop-Location }
