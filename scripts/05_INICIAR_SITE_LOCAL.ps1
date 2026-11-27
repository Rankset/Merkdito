# Exibe o HTML original em http://localhost:5500, com CORS configurado na API.
$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$env:MERKDITO_FRONTEND_PATH = Join-Path $base 'Frontend'
$proj = Join-Path $base 'scripts/site_local/Merkdito.SiteLocal.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK nao encontrado.' }
Write-Host 'Site comercial local: http://localhost:5500/index.html' -ForegroundColor Cyan
Write-Host 'Nao feche este PowerShell durante os testes.'
& dotnet run --project $proj
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel iniciar o site local.' }
