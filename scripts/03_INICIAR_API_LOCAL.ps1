# A API fica restrita a localhost. O celular acessa via adb reverse USB.
$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $base 'Merkdito.Api/Merkdito.Api.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK nao encontrado.' }
Write-Host 'Iniciando API do Merkdito em http://localhost:5102' -ForegroundColor Cyan
Write-Host 'Deixe esta janela ABERTA enquanto testar o aplicativo.'
& dotnet run --project $proj --launch-profile http
if ($LASTEXITCODE -ne 0) { throw 'API terminou com erro. Confira SQL Server e Jwt:Key.' }
