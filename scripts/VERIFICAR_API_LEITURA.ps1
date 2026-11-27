param([string]$ApiUrl = 'http://localhost:5102')
$ErrorActionPreference = 'Stop'
Write-Host 'Merkdito - verificacao nao destrutiva (leitura)' -ForegroundColor Cyan
try {
  $health = Invoke-RestMethod -Uri ($ApiUrl.TrimEnd('/') + '/healthz') -TimeoutSec 10
  Write-Host "Health: $($health.status)" -ForegroundColor Green
  $lojas = @(Invoke-RestMethod -Uri ($ApiUrl.TrimEnd('/') + '/api/consumidor/lojas') -TimeoutSec 15)
  Write-Host "Lojas de venda ativas: $($lojas.Count)" -ForegroundColor Green
  $lojas | Select-Object -First 10 id,empresaId,empresa,nome | Format-Table -AutoSize
  Write-Host 'Os testes de cadastro/venda NAO foram executados (alteram o banco).' -ForegroundColor Yellow
} catch {
  Write-Host "Falha: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
