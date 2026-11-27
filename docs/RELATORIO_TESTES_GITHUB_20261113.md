# Merkdito — Relatório de testes da integração local (v1.4)

**Data:** 13 de novembro de 2026  
**Fonte:** `Merkdito_Integracao_Local_USB_v1_4_20261113.zip` (SHA-256 `4cc7a3c44355a1f1c0f89328ba57868464cea8950dd64fd55abdbd6200e2c0f5`).

## Escopo efetivamente executado

O GitHub Actions executou a API ASP.NET Core .NET 10 com **SQL Server 2022 temporário**. Aplicou os scripts `00`, `01`, `02` num banco descartável. Usou somente empresas, consumidores e estoques fictícios. O site HTML foi servido via HTTP e a aplicação de gestão .NET foi compilada.

**Integração E2E real: 41/41 verificações aprovadas** na v1.4. O teste conferiu que dados criados pela API administrativa apareciam na API consumida pelo app e que venda fictícia alterou estoque e surgiu no histórico e na consulta empresarial. O teste confirmou bloqueio de compra sem meio de pagamento, proteção multiempresa, autorização por vínculo e idempotência.

Verificações estáticas sobre **o ZIP v1.4**: 59/59 de integração + 20/20 de modo local aprovadas.

### Evidências oficiais

- [GitHub Actions — integração v1.4 e banco SQL real](https://github.com/NicolasSantos-Dev/-merkdito-app/actions/runs/43455058221)
- [GitHub Actions — integração v1.3 de controle](https://github.com/NicolasSantos-Dev/-merkdito-app/actions/runs/43454664764)
- [GitHub Actions — APK da v1.4](https://github.com/NicolasSantos-Dev/-merkdito-app/actions/runs/43455183878)
- Arquivo `e2e_result.json` anexo, com 41 verificações numeradas e status.

## Interpretação correta

**APROVADO EM CI:** compilação da solução .NET, inicialização SQL Server, aplicação do esquema, testes de API e banco e fluxo HTTP via endpoints usados pelo site e pelo Flutter.

**NÃO COMPROVADO:** operação em dispositivo Android físico via ADB, páginas web interagidas por usuário real, estabilidade de uma instalação SQL Server Express do notebook da equipe, pagamento real e liberação automática de retirada, distribuição comercial, segurança de produção e conexão à base do colega já existente.

Para demonstrar o app no PIM, executar a API e o banco no mesmo notebook e, após `adb devices`, usar `adb reverse tcp:5102 tcp:5102`. No celular, testar `http://127.0.0.1:5102/healthz` no navegador antes de cadastrar-se. O APK não contém banco nem API, e não acessará empresas sem o notebook ligado e o encaminhamento USB.

O modo de venda fictícia reduz estoque e só pode ser habilitado para laboratório isolado. Não é pagamento aprovado. Não utilizar com mercadorias ou clientes reais.

## Sumário de controles E2E (ver JSON para todos os 41)

1. Banco criado do zero a partir dos três scripts oficiais.
2. Autenticação ADMIN, CLIENTE (empresa) e CONSUMIDOR separada por token.
3. Duas empresas, três PDVs e catálogo individualizado por estoque e preço.
4. Cadastro/login consumidor, múltiplos vínculos, atualização e bloqueio de loja não autorizada.
5. Segurança: endpoints com 401/403/404, login inválido e isolamento entre empresas.
6. Compra de laboratório: bloqueio financeiro no modo normal; modo demo gerando uma venda de R$ 9,90, uma redução de estoque; reenvio idempotente e conflito de composição.
7. Mesma venda visível ao consumidor e à empresa; loja B não perdeu estoque.
8. Páginas do site comercial disponíveis via servidor HTTP.


## Correção de compilação no Windows

A primeira execução do job de scripts no Windows encontrou conflito de arquivos top-level no gerador de hash do administrador, porque o projeto varria recursivamente a pasta `scripts/site_local`. A revisão v1.5 limita explicitamente os itens `Compile` a `Program.cs` e `Merkdito.Api/Security/PasswordHashing.cs`. Essa correção precisa ser confirmada por nova execução GitHub Actions.
