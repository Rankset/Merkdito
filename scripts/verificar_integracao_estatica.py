#!/usr/bin/env python3
"""Fail-fast offline source contract audit. Not an E2E or build test."""
from pathlib import Path
import re, subprocess, sys, json, zipfile
r=Path(__file__).resolve().parent.parent
checks=[]
def require(ok,description):
    checks.append((bool(ok),description))
    if not ok: print('FAIL:',description)
# Explicit source provenance
for p in ['Merkdito.sln','Merkdito.Api/Merkdito.Api.csproj','Merkdito.Data/Merkdito.Data.csproj',
 'Merkdito.Web/Merkdito.Web.csproj','Merkdito.Web/Components/Pages/Clientes.razor',
 'Frontend/index.html','Frontend/lojas.html','Frontend/produtos.html','Frontend/vendas.html',
 'Frontend/compras_app.html','app_flutter/pubspec.yaml','app_flutter/lib/main.dart',
 'sql/00_MerkditoDb_original.sql','sql/01_Merkdito_Consumidor_App.sql',
 'sql/02_Merkdito_Vinculos_PDV.sql']:
    require((r/p).is_file(),p+' existe')
controller=(r/'Merkdito.Api/Controllers/ConsumidorController.cs').read_text(encoding='utf-8')
frontend_js=(r/'Frontend/assets/js/compras_app.js').read_text(encoding='utf-8')
sales=(r/'Merkdito.Api/Controllers/VendasController.cs').read_text(encoding='utf-8')
state=(r/'app_flutter/lib/state/merkdito_state.dart').read_text(encoding='utf-8')
api=(r/'app_flutter/lib/services/merkdito_api.dart').read_text(encoding='utf-8')
ctx=(r/'Merkdito.Data/Context/MerkditoDbContext.cs').read_text(encoding='utf-8')
sql=(r/'sql/02_Merkdito_Vinculos_PDV.sql').read_text(encoding='utf-8')
for route in ['lojas','cadastro','login','perfil','vinculos','lojas/{lojaId:int}/produtos','pedidos']:
    require(route in controller,'api consumidor rota '+route)
for entity in ['Clientes','Estabelecimentos','Produtos','ProdutosEstabelecimentos','Vendas','ItensVenda','MovimentacoesEstoque','Consumidores','PedidosConsumidores','ConsumidorEstabelecimentos']:
    require(('DbSet<'+entity.rstrip('s')+'>') in ctx or entity in ctx,'db context '+entity)
require('!x.EhEstoqueCentral' in controller,'não divulgar estoque central como loja')
require('x.Produto.ClienteId == x.Estabelecimento!.ClienteId' in controller,'catálogo isolado pela empresa da loja')
require('PodeAcessarLoja' in controller and 'Forbid()' in controller,'autorização por vínculo de PDV')
require('IsolationLevel.Serializable' in controller,'venda transacional')
require('QuantidadeEstoque -= i.Quantidade' in controller,'estoque da loja debitado')
require('IdempotencyKey' in controller,'proteção de duplicidade')
require('PermitirVendaSemPagamento' in controller and 'StatusCode(503' in controller,'produção bloqueia venda sem provedor')
require('Status = "REGISTRADA"' in controller,'venda sem pagamento não marcada como paga')
require('ExecuteUpdateAsync' in controller and 'EstabelecimentoId, ids[0]' in controller,'legacy StoreId acompanha vínculos')
require('APP_CONSUMIDOR' in controller and 'APP_CONSUMIDOR' in frontend_js,'site identifica compras do aplicativo')
require('origem = null' in sales and 'v.Origem == origemExata' in sales,'filtro por origem no servidor')
require('consulta = consulta.Where(v => !(v.Origem == "APP_CONSUMIDOR" && v.Status == "REGISTRADA"));' in sales,'venda de demo não é contada no relatório financeiro')
require('MerkditoAuth.apiFetch' in frontend_js,'consulta site usando autenticação atual')
require('document.createElement' in frontend_js and '.textContent' in frontend_js,'renderização site sem HTML inseguro')
require('$_' not in frontend_js and 'eval(' not in frontend_js,'sem código arbitrário no painel')
require('flutter_secure_storage' in (r/'app_flutter/pubspec.yaml').read_text(), 'sessão mobile protegida')
require("/api/consumidor/lojas/$lojaId/produtos" in api,'app consulta produto do PDV selecionado')
require('api.registrarVenda' in state,'app grava venda via API')
require("carrinho.clear(); produtos.clear();" in state,'troca de loja limpa a sacola')
require("_storage.write(key: 'consumer_token'" in state,'persistência do JWT sem gravar senha')
require('FOREIGN KEY(EstabelecimentoId)' in sql,'SQL vínculos por PDV com FK')
for p in ['Merkdito.Api/appsettings.json','Merkdito.Web/appsettings.json']:
 t=(r/p).read_text()
 require('PasswordHash' not in t and '"Key"' not in t,'segredos não incluídos: '+p)
require(not list(r.rglob('Senha.txt')),'senha antiga não empacotada')
require(not list(r.rglob('*.dll')) and not list(r.rglob('*.pdb')),'sem DLL/PDB original empacotados')
# Ensure all script tags/links to LOCAL assets are grounded in reconstructed frontend.
files=list((r/'Frontend').glob('*.html'))
missing=set();n_refs=0
for p in files:
 s=p.read_text(encoding='utf-8',errors='replace')
 for a in re.findall(r'(?:src|href)\s*=\s*["\'](assets/[^"\']+)',s,re.I):
  a=a.split('?')[0].split('#')[0];n_refs+=1
  if not (r/'Frontend'/a).exists():missing.add((p.name,a))
require(not missing,'HTML references to local assets valid, missing='+str(sorted(missing)[:8]))
try:
 subprocess.run(['node','--check',str(r/'Frontend/assets/js/compras_app.js')],check=True,capture_output=True,text=True)
 checks.append((True,'new JavaScript parses (node --check)'))
except (OSError,subprocess.CalledProcessError) as e:checks.append((False,'new JavaScript parses '+str(e)))
count_pass=sum(ok for ok,_ in checks)
report={'checks':len(checks),'passed':count_pass,'failed':len(checks)-count_pass,
        'html_files':len(files),'html_local_asset_refs':n_refs,
        'failures':[desc for ok,desc in checks if not ok]}
(r/'docs').mkdir(exist_ok=True)
(r/'docs/VALIDACAO_ESTATICA.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
sys.exit(0 if all(ok for ok,_ in checks) else 1)
