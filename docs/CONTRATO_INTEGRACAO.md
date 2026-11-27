# Contrato do projeto integrado

## Identidade e empresa/PDV

- **ADMIN**: painel Blazor, gerencia empresas (`api/Clientes`), preservando autenticação original.
- **CLIENTE** (empresa): frontend comercial, administra lojas (`api/Lojas`), produtos (`api/Produtos` e `api/ProdutosEstabelecimentos`), estoque e vendas (`api/Vendas`).
- **CONSUMIDOR**: perfil próprio do app (`api/consumidor/*`), vinculado a até 20 PDVs (de diversas empresas); não recebe permissões CLIENTE.

| Método e rota | Acesso | Origem / destino real |
|---|---|---|
| `GET /api/consumidor/lojas` | Público | Lista `Estabelecimentos` ativos, não centrais, com `Cliente` ativo |
| `POST /api/consumidor/cadastro` | Público, com limite de tentativas | Cria `Consumidores` e `ConsumidorEstabelecimentos` |
| `POST /api/consumidor/login` | Público, com limite de tentativas | Verifica hash e gera JWT consumidor |
| `GET /api/consumidor/perfil` | Consumidor | Dados da conta e PDVs autorizados |
| `PUT /api/consumidor/vinculos` | Consumidor | Altera autorizações, mantém o campo legado de PDV em sincronia |
| `GET /api/consumidor/lojas/{lojaId}/produtos` | Consumidor vinculado | `ProdutosEstabelecimentos`, `Produtos`, `Categorias`, preço e quantidade do PDV |
| `POST /api/consumidor/pedidos` | Consumidor vinculado, demo ativada | Revalida item/saldo; cria `Vendas`, `ItensVenda`, `MovimentacoesEstoque`, `PedidosConsumidores` |
| `GET /api/consumidor/pedidos` | Consumidor | Histórico do próprio usuário via `PedidosConsumidores` |
| `GET /api/Vendas?origem=APP_CONSUMIDOR` | Empresa (CLIENTE) | Vendas originadas do app **da própria empresa** |
| `GET /api/Vendas/{id}` | Empresa (CLIENTE) | Detalhe e itens da venda autorizada |

## Exemplo real do contrato de compra (somente ambiente demo)

`POST /api/consumidor/pedidos` com Bearer do consumidor:

```json
{
  "estabelecimentoId": 12,
  "idempotencyKey": "9d286720-2d09-4da2-ad13-a2af40f0d150",
  "itens": [{"produtoId": 31, "quantidade": 1}]
}
```

IDs **meramente ilustrativos**. Preço total não vem do telefone: é calculado no servidor. A resposta contém `pedidoId`, `total`, `status` (`REGISTRADA`) e `repetido`.

## Regras de consistência

- Empresas e PDVs **não são duplicados** no app; vêm diretamente do cadastro do site.
- O mesmo produto pode ter preços e quantidades diferentes em PDVs diferentes; o app usa os valores de `ProdutosEstabelecimentos`.
- O consumidor só compra num PDV ao qual está vinculado; o servidor valida essa relação em toda consulta e venda.
- A troca de PDV limpa a sacola e a chave de idempotência local.
- A venda do app aparece no site porque usa `Vendas` e `ItensVenda` da plataforma, com origem `APP_CONSUMIDOR`.
- Sem integração de provedor de pagamento, a operação permanece bloqueada fora do laboratório e o status não comprova pagamento nem retirada autorizada.

## Origem do código

A API existente foi reconstruída do ZIP original achatado; `ConsumidorController`, novas entidades, migrations e ajustes de integração vêm da evolução da app Flutter 1.2 e do pacote presente. Não consta prova de que esses arquivos estejam implantados no servidor original.
