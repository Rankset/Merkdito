(function(){"use strict";
const $=id=>document.getElementById(id);let pagina=1, total=0;const tam=20;
const moeda=new Intl.NumberFormat('pt-BR',{style:'currency',currency:'BRL'});
function limpar(node){node.replaceChildren();}
function cel(tr,value){const c=document.createElement('td');c.textContent=String(value??'');tr.appendChild(c);return c;}
async function json(resp){if(!resp.ok)throw Error('A consulta não foi concluída (HTTP '+resp.status+').');return resp.json();}
async function carregarLojas(){const r=await MerkditoAuth.apiFetch('Lojas');const d=await json(r);
const lojas=Array.isArray(d)?d:(d.lojas||[]);for(const l of lojas){if(l.ehEstoqueCentral)continue;
const option=document.createElement('option');option.value=String(l.id);option.textContent=l.nomeFantasia;
$('filtroLoja').appendChild(option);}}
async function carregar(){const btn=$('atualizar');btn.disabled=true;$('estado').textContent='Carregando registros do aplicativo…';
try{const q=new URLSearchParams({pagina:String(pagina),tamanhoPagina:String(tam),origem:'APP_CONSUMIDOR'});
if($('filtroLoja').value)q.set('estabelecimentoId',$('filtroLoja').value);
if($('de').value)q.set('de',$('de').value);if($('ate').value)q.set('ate',$('ate').value);
const r=await MerkditoAuth.apiFetch('Vendas?'+q.toString());const d=await json(r);
const linhas=$('linhas');limpar(linhas);total=d.totalRegistros||0;
for(const v of (d.vendas||[])){const tr=document.createElement('tr');
cel(tr,v.id);cel(tr,new Date(v.dataVendaUtc).toLocaleString('pt-BR'));
cel(tr,v.estabelecimento);cel(tr,v.origem);cel(tr,v.status);cel(tr,v.quantidadeItens);cel(tr,moeda.format(v.total));
const action=cel(tr,'');const link=document.createElement('button');link.type='button';link.textContent='Detalhes';
link.addEventListener('click',()=>verDetalhes(v.id));action.appendChild(link);linhas.appendChild(tr);}
if(!d.vendas?.length){const tr=document.createElement('tr');const c=cel(tr,'Nenhuma compra do app encontrada.');c.colSpan=8;linhas.appendChild(tr);}
$('estado').textContent=total+' registro(s) do aplicativo.';
$('pagina').textContent='Página '+pagina+' de '+Math.max(1,Math.ceil(total/tam));
$('anterior').disabled=pagina<=1;$('proxima').disabled=pagina>=Math.ceil(total/tam);
}catch(e){$('estado').textContent=e.message;$('estado').className='error';}finally{btn.disabled=false;}}
async function verDetalhes(id){try{const r=await MerkditoAuth.apiFetch('Vendas/'+encodeURIComponent(id));const d=await json(r);
// Never display raw API payload as HTML; all content is inserted as text.
$('detalhes').textContent='Compra: #'+d.id+'\nPDV: '+(d.estabelecimento||'')+'\nStatus: '+d.status+
'\nPagamento: NÃO COMPROVADO por este status\nValor: '+moeda.format(d.total)+'\n\nItens:\n'+
(d.itens||[]).map(i=>i.nomeProduto+' × '+i.quantidade+' — '+moeda.format(i.totalItem)).join('\n');
}catch(e){$('detalhes').textContent=e.message;}}
$('atualizar').addEventListener('click',()=>{pagina=1;carregar()});
$('anterior').addEventListener('click',()=>{if(pagina>1){pagina--;carregar()}});
$('proxima').addEventListener('click',()=>{if(pagina<Math.ceil(total/tam)){pagina++;carregar()}});
for(const name of ['de','ate','filtroLoja'])$(name).addEventListener('change',()=>{pagina=1;carregar()});
if(MerkditoAuth.requireClient())carregarLojas().then(carregar).catch(e=>{$('estado').textContent=e.message;});
})();
