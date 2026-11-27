# Merkdito Flutter - Mercado Autônomo v1.2

Projeto editável para Android. Código de cadastro múltiplo de PDVs, seleção da unidade, catálogo via API, carrinho, histórico e registro de venda de laboratório. Não possui dados comerciais estáticos no fluxo principal.

- Para executar, siga `../docs/COMO_EXECUTAR.md`.
- Variável de compilação: `--dart-define=MERKDITO_API_URL=http://127.0.0.1:5102` com `adb reverse` em teste físico.
- Use **HTTPS real** para ambientes remotos; HTTP está liberado apenas no manifesto debug.
- O modo laboratório não representa cobrança nem autorização comercial de retirada.
