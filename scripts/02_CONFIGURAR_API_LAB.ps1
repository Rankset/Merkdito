# Configura um laboratório local isolado, SEM criar bancos e SEM publicar portas.
param([string]$InstanciaSql = 'localhost\SQLEXPRESS')
$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $base 'Merkdito.Api/Merkdito.Api.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instale .NET SDK 10 antes de continuar.' }
if (-not (Test-Path $projeto)) { throw "Projeto da API ausente: $projeto" }
if ($InstanciaSql -match '[;\r\n]') { throw 'Nome de instancia SQL invalido.' }
$connection = "Server=$InstanciaSql;Database=MerkditoDb;Trusted_Connection=True;TrustServerCertificate=True;"
& dotnet user-secrets set 'ConnectionStrings:MerkditoConnection' $connection --project $projeto
if ($LASTEXITCODE -ne 0) { throw 'Falha ao definir conexão com SQL Server.' }
# O segredo nunca é gravado em arquivos do projeto nem enviado ao GitHub.
$existente = @(& dotnet user-secrets list --project $projeto 2>$null | Where-Object { $_ -match '^Jwt:Key\s*=' })
if ($existente.Count -eq 0) {
  $bytes = New-Object byte[] 48
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
  $chave = [Convert]::ToBase64String($bytes)
  & dotnet user-secrets set 'Jwt:Key' $chave --project $projeto
  if ($LASTEXITCODE -ne 0) { throw 'Falha ao salvar JWT.' }
  $chave = $null
  Write-Host 'Chave JWT exclusiva de laboratorio criada em User Secrets.' -ForegroundColor Green
} else { Write-Host 'JWT existente preservado (nao revoga sessoes).' -ForegroundColor Green }
Write-Host 'Conexao de laboratorio configurada. O banco/tabelas precisam existir.' -ForegroundColor Green
Write-Host 'Se for um banco NOVO, execute os scripts 00, 01 e 02 uma unica vez pelo SSMS.'
Write-Host 'O modo de venda ficticia permanece DESABILITADO por padrao.' -ForegroundColor Yellow
