<#
  Run ONLY on a lab database populated via the actual site Merkdito.
  The test uses the site's CLIENTE login, the consumer API, and the sales endpoints.
  A fake consumer is created. With -RegistrarVendaTeste, ONE stock unit is deducted.
#>
param(
  [string]$ApiUrl = 'http://localhost:5102',
  [Parameter(Mandatory=$true)][string]$EmailEmpresa,
  [switch]$RegistrarVendaTeste
)
$ErrorActionPreference='Stop'
$ApiUrl=$ApiUrl.TrimEnd('/')
function Confirmar([bool]$Condicao,[string]$Descricao){
  if(-not $Condicao){throw "FALHOU: $Descricao"}
  Write-Host "OK: $Descricao" -ForegroundColor Green
}
function CallApi([string]$Metodo,[string]$Rota,$Corpo=$null,[string]$Token=''){
  $args=@{Uri="$ApiUrl$Rota";Method=$Metodo;TimeoutSec=30;Headers=@{Accept='application/json'}}
  if($Token){$args.Headers.Authorization="Bearer $Token"}
  if($null -ne $Corpo){$args.ContentType='application/json';$args.Body=ConvertTo-Json -InputObject $Corpo -Depth 8 -Compress}
  return Invoke-RestMethod @args
}
Write-Host 'Teste Site ↔ App Merkdito (somente ambiente de homologação)' -ForegroundColor Cyan
$h=CallApi GET /healthz
Confirmar ($h.status -eq 'ok') 'API levantada'
$segura=Read-Host 'Senha da conta CLIENTE do site Merkdito' -AsSecureString
$bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura)
try{$senhaEmpresa=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)}
try{$loginEmpresa=CallApi POST /api/Auth/login @{email=$EmailEmpresa;senha=$senhaEmpresa}}
finally{$senhaEmpresa=$null}
Confirmar ($loginEmpresa.perfil -eq 'CLIENTE') 'Login na conta empresarial do SITE (perfil CLIENTE)'
$tokenEmpresa=$loginEmpresa.token
$siteLojas=@(CallApi GET /api/Lojas $null $tokenEmpresa | Where-Object { $_.ativo -and -not $_.ehEstoqueCentral })
Confirmar ($siteLojas.Count -gt 0) 'A empresa cadastrou pelo menos um PDV ativo no site'
$lojasPublicas=@(CallApi GET /api/consumidor/lojas)
$siteIds=@($siteLojas | ForEach-Object { [int]$_.id })
foreach($loja in $siteLojas){
  Confirmar (@($lojasPublicas | Where-Object {$_.id -eq $loja.id -and $_.empresaId -eq $loginEmpresa.clienteId}).Count -eq 1) "PDV do SITE aparece no app: $($loja.nomeFantasia)"
}
$ids=@($siteIds | Select-Object -First 2)
$rand=[Guid]::NewGuid().ToString('N').Substring(0,14)
$emailConsumidor="tcc.integracao.$rand@example.invalid"
$senhaConsumidor="TccLab-$rand-1234"
$cadastro=CallApi POST /api/consumidor/cadastro @{
 nome='Consumidor Teste PIM';email=$emailConsumidor;senha=$senhaConsumidor;estabelecimentoIds=$ids
}
Confirmar (-not [string]::IsNullOrWhiteSpace($cadastro.token)) 'Cadastro de consumidor separado da conta empresarial'
$loginConsumidor=CallApi POST /api/consumidor/login @{email=$emailConsumidor;senha=$senhaConsumidor}
$senhaConsumidor=$null
Confirmar (-not [string]::IsNullOrWhiteSpace($loginConsumidor.token)) 'Login consumidor armazenado no banco e token emitido'
$tokenConsumidor=$loginConsumidor.token
$perfil=CallApi GET /api/consumidor/perfil $null $tokenConsumidor
Confirmar ($perfil.estabelecimentoIds.Count -eq $ids.Count) 'Vínculos consumidor ↔ PDVs retornaram do banco'
foreach($id in $ids){
  $dadosSite=CallApi GET "/api/ProdutosEstabelecimentos?estabelecimentoId=$id" $null $tokenEmpresa
  $dadosApp=@(CallApi GET "/api/consumidor/lojas/$id/produtos" $null $tokenConsumidor)
  $esperados=@($dadosSite.itens | Where-Object {$_.ativoNaLoja -and $_.produtoAtivo -and [decimal]$_.precoVenda -ge 0})
  Confirmar ($dadosApp.Count -eq $esperados.Count) "Catálogo do PDV $id corresponde ao SITE ($($dadosApp.Count) produtos)"
  foreach($p in $dadosApp){
    $siteP=@($esperados | Where-Object { [int]$_.produtoId -eq [int]$p.id })
    Confirmar ($siteP.Count -eq 1 -and [decimal]$siteP[0].precoVenda -eq [decimal]$p.preco -and
      [int]$siteP[0].quantidadeEstoque -eq [int]$p.estoque) "Preço e estoque do site iguais ao app (produto $($p.id))"
  }
}
if($siteIds.Count -gt $ids.Count){
  $semVinculo=($siteIds | Where-Object {$_ -notin $ids} | Select-Object -First 1)
  try { CallApi GET "/api/consumidor/lojas/$semVinculo/produtos" $null $tokenConsumidor | Out-Null
    throw 'ACESSO INDEVIDO: consumidor leu PDV sem vínculo'
  } catch {
    if($_.Exception.Message -like 'ACESSO INDEVIDO*'){throw}
    # Receive 403 (Forbidden) without suppressing unrelated HTTP errors.
    $status=$_.Exception.Response.StatusCode.value__
    Confirmar ($status -eq 403) 'Consumidor não consegue consultar loja não vinculada (403)'
  }
}
if($RegistrarVendaTeste){
  $pdv=$ids[0]
  $antes=@(CallApi GET "/api/consumidor/lojas/$pdv/produtos" $null $tokenConsumidor)
  $produto=@($antes | Where-Object {$_.estoque -gt 0} | Select-Object -First 1)
  Confirmar ($produto.Count -gt 0) 'Produto com estoque disponível para venda de laboratório'
  $idProduto=[int]$produto[0].id;$estoqueAntes=[int]$produto[0].estoque
  $req=@{estabelecimentoId=$pdv;idempotencyKey=[Guid]::NewGuid().ToString();itens=@(@{produtoId=$idProduto;quantidade=1})}
  $pedido=CallApi POST /api/consumidor/pedidos $req $tokenConsumidor
  Confirmar ($pedido.pedidoId -gt 0 -and $pedido.status -eq 'REGISTRADA') 'Venda teste registrada SEM pagamento'
  $repetido=CallApi POST /api/consumidor/pedidos $req $tokenConsumidor
  Confirmar ($repetido.repetido -and $repetido.pedidoId -eq $pedido.pedidoId) 'Idempotência impede dupla venda'
  $depois=@(CallApi GET "/api/consumidor/lojas/$pdv/produtos" $null $tokenConsumidor)
  $produtoDepois=@($depois | Where-Object {$_.id -eq $idProduto})[0]
  Confirmar ($produtoDepois.estoque -eq ($estoqueAntes-1)) 'Estoque do mesmo PDV foi reduzido em uma unidade'
  $site=CallApi GET '/api/Vendas?pagina=1&tamanhoPagina=200&origem=APP_CONSUMIDOR' $null $tokenEmpresa
  Confirmar (@($site.vendas | Where-Object {$_.id -eq $pedido.pedidoId}).Count -eq 1) 'Venda do APP aparece no SITE'
  $detalhe=CallApi GET "/api/Vendas/$($pedido.pedidoId)" $null $tokenEmpresa
  Confirmar ($detalhe.estabelecimentoId -eq $pdv -and $detalhe.itens.Count -eq 1) 'Site vê os mesmos itens e o mesmo PDV do pedido'
  $hist=@(CallApi GET /api/consumidor/pedidos $null $tokenConsumidor)
  Confirmar (@($hist | Where-Object {$_.pedidoId -eq $pedido.pedidoId}).Count -eq 1) 'O app vê a venda no próprio histórico'
}else{
  Write-Host 'Vendas não alteradas. -RegistrarVendaTeste exige modo demo habilitado na API e ESTOQUE FICTÍCIO.' -ForegroundColor Yellow
}
Write-Host 'Conferências de contrato finalizadas. Não substituem homologação com Android físico.' -ForegroundColor Cyan
