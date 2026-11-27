# Executar antes de conectar o celular. Usa leitura publica do catálogo de PDVs.
param([string]$ApiUrl = 'http://localhost:5102')
$ErrorActionPreference = 'Stop'
$ApiUrl = $ApiUrl.TrimEnd('/')
if ($ApiUrl -notin @('http://localhost:5102','http://127.0.0.1:5102')) {
  Write-Warning 'Este script foi desenhado para a API LOCAL de laboratório.'
}
Write-Host '1/2 Verificando processo da API...' -ForegroundColor Cyan
try { $saude = Invoke-RestMethod "$ApiUrl/healthz" -TimeoutSec 10 } catch { throw "API inacessivel. Inicie 03_INICIAR_API_LOCAL.ps1. Detalhes: $($_.Exception.Message)" }
if ($saude.status -ne 'ok') { throw 'A rota /healthz nao retornou status ok.' }
Write-Host 'OK: API responde em localhost.' -ForegroundColor Green
Write-Host '2/2 Consultando PDVs no SQL Server (isto valida o caminho API->banco)...' -ForegroundColor Cyan
try { $lojas = @(Invoke-RestMethod "$ApiUrl/api/consumidor/lojas" -TimeoutSec 20) }
catch { throw "API responde, mas falhou ao consultar SQL Server: $($_.Exception.Message)" }
Write-Host "OK: Consulta ao banco concluida. PDVs ativos retornados: $($lojas.Count)" -ForegroundColor Green
if ($lojas.Count -eq 0) { Write-Warning 'Nenhum PDV retornado. Cadastre CLIENTE, PDV ativo e estoque pelo site antes de testar o cadastro do app.' }
else { $lojas | Select-Object id,empresa,nome | Format-Table -AutoSize }
