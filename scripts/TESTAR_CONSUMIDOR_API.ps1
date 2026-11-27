param(
    [string]$ApiUrl = "http://localhost:5102",
    [switch]$RegistrarVendaDeTeste
)
$ErrorActionPreference = 'Stop'
$ApiUrl = $ApiUrl.TrimEnd('/')
function Assert-Check([bool]$ok, [string]$message) {
    if (-not $ok) { throw "FALHOU: $message" }
    Write-Host "OK: $message" -ForegroundColor Green
}
function RequestJson([string]$method, [string]$path, $body = $null, [string]$token = '') {
    $headers = @{ Accept = 'application/json' }
    if ($token) { $headers.Authorization = "Bearer $token" }
    $requestArgs = @{ Uri="$ApiUrl$path"; Method=$method; Headers=$headers; TimeoutSec=30 }
    if ($null -ne $body) {
        $requestArgs.ContentType = 'application/json'
        $requestArgs.Body = ConvertTo-Json -Depth 10 -Compress -InputObject $body
    }
    return Invoke-RestMethod @requestArgs
}
Write-Host 'Merkdito: integracao API consumidor em ambiente de TESTE' -ForegroundColor Cyan
$health = RequestJson 'GET' '/healthz'
Assert-Check ($health.status -eq 'ok') 'API responde /healthz'
$lojas = @(RequestJson 'GET' '/api/consumidor/lojas')
Assert-Check ($lojas.Count -gt 0) 'Site/API retorna lojas para cadastro'
$ids = @($lojas | Select-Object -First 2 | ForEach-Object { [int]$_.id })
$guid = [guid]::NewGuid().ToString('N').Substring(0,12)
$usuario = "pim.automatizado.$guid@example.invalid"
$senha = "TesteApenas-$guid"
$cadastrar = @{ nome='Teste Merkdito PIM';email=$usuario;senha=$senha;estabelecimentoIds=$ids }
$session = RequestJson 'POST' '/api/consumidor/cadastro' $cadastrar
Assert-Check (-not [string]::IsNullOrWhiteSpace($session.token)) 'Cadastro cria conta e fornece JWT'
$perfil = RequestJson 'GET' '/api/consumidor/perfil' $null $session.token
Assert-Check ($perfil.estabelecimentoIds.Count -eq $ids.Count) 'Vinculos multi-PDV persistidos'
$login = RequestJson 'POST' '/api/consumidor/login' @{email=$usuario;senha=$senha}
Assert-Check (-not [string]::IsNullOrWhiteSpace($login.token)) 'Login com credenciais do banco'
$pdv = $ids[0]
$catalogo = @(RequestJson 'GET' "/api/consumidor/lojas/$pdv/produtos" $null $login.token)
Write-Host "Produtos recebidos para PDV #$pdv`: $($catalogo.Count)"
$novos = RequestJson 'PUT' '/api/consumidor/vinculos' @{estabelecimentoIds=@($pdv)} $login.token
Assert-Check (($novos.estabelecimentoIds.Count -eq 1) -and ($novos.estabelecimentoIds[0] -eq $pdv)) 'Atualizacao de vinculos aceita 1 PDV'
if ($ids.Count -gt 1) {
    try {
        RequestJson 'GET' "/api/consumidor/lojas/$($ids[1])/produtos" $null $login.token | Out-Null
        throw 'ERRO: consulta a PDV removido foi aceita'
    } catch {
        if ($_.Exception.Message -match 'ERRO:') { throw }
        Assert-Check ($true) 'PDV removido fica sem autorizacao'
    }
}
if ($RegistrarVendaDeTeste) {
    Write-Host 'ATENCAO: a venda de teste reduz estoque e cria registros de banco.' -ForegroundColor Yellow
    $produto = $catalogo | Where-Object { $_.estoque -gt 0 } | Select-Object -First 1
    if (-not $produto) { Write-Host 'Nao ha produto com estoque no PDV; venda ignorada.'; exit 0 }
    $chave = [guid]::NewGuid().ToString()
    $pedido = @{estabelecimentoId=$pdv;idempotencyKey=$chave;itens=@(@{produtoId=[int]$produto.id;quantidade=1})}
    $venda = RequestJson 'POST' '/api/consumidor/pedidos' $pedido $login.token
    Assert-Check ($venda.pedidoId -gt 0) 'Venda de LABORATORIO gravada; nao houve pagamento'
    $replay = RequestJson 'POST' '/api/consumidor/pedidos' $pedido $login.token
    Assert-Check (($replay.pedidoId -eq $venda.pedidoId) -and ($replay.repetido -eq $true)) 'Idempotencia preserva numero da venda'
    $historico = @(RequestJson 'GET' '/api/consumidor/pedidos' $null $login.token)
    Assert-Check (@($historico | Where-Object {$_.pedidoId -eq $venda.pedidoId}).Count -eq 1) 'Historico contem a venda uma unica vez'
} else {
    Write-Host 'Venda nao executada. Use -RegistrarVendaDeTeste APENAS com dados ficticios e flag de laboratorio.' -ForegroundColor Yellow
}
Write-Host 'Fluxo de consulta/cadastro/login/vinculos executado; veja resultados acima.' -ForegroundColor Cyan
