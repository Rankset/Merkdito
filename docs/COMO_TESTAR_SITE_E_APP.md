# Homologação: site Merkdito ↔ aplicativo Android

## Pré-requisitos

Windows com .NET 10 SDK, SQL Server Express e SSMS, Flutter SDK, Android SDK e uma conta administrativa criada para laboratório. Use somente **banco de testes**; faça backup antes de qualquer alteração no banco já existente. O ZIP original foi achatado, de modo que a solução reorganizada precisa ser compilada e comparada com sua fonte de produção.

## 1. Estrutura do banco

**Instalação nova:** em SQL Server de testes, execute `sql/00_MerkditoDb_original.sql` no SSMS; depois `sql/01_Merkdito_Consumidor_App.sql`; por último `sql/02_Merkdito_Vinculos_PDV.sql`. Não execute `00` se o banco já existir, nem reaplique os scripts 01/02 se as tabelas estiverem presentes. **Não misture o uso de scripts manuais com `dotnet ef database update` para as mesmas migrações.**

**Banco já existente:** não rode os scripts diretamente. Confira backups, `__EFMigrationsHistory`, esquema, compatibilidade, plano de alteração e rollback antes de adicionar as tabelas do consumidor.

## 2. Subir a API

```powershell
cd C:\MerkditoIntegrado\Merkdito.Api
# Requer .NET 10 e SQL Server Express instalados.
dotnet restore ..\Merkdito.sln
dotnet build ..\Merkdito.sln
# Alterar string de conexão local caso sua instância use outro nome:
# dotnet user-secrets set "ConnectionStrings:MerkditoConnection" "Server=...;Database=MerkditoDb;..."
# Gerar uma chave JWT exclusiva de LABORATÓRIO:
$bytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes); $rng.Dispose()
dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String($bytes))
Remove-Variable bytes
# Para AdminAuth, use o gerador interativo de hash (não coloque senha pura no repo):
# dotnet run --project ..\scripts\GERAR_HASH_ADMIN.csproj
# dotnet user-secrets set "AdminAuth:Email" "admin.exemplo@dominio.invalid"
# dotnet user-secrets set "AdminAuth:PasswordHash" "<hash gerado>"
dotnet run --launch-profile http
```

Abra `http://localhost:5102/healthz` e `http://localhost:5102/swagger` (Swagger apenas desenvolvimento). `healthz` comprova **apenas que o servidor respondeu**, não garante integridade do banco.

## 3. Executar o site original reconstruído

Em **outro** terminal:

```powershell
cd C:\MerkditoIntegrado\Merkdito.Web
dotnet run --launch-profile http
```

Painel geral de clientes: `http://localhost:5254/admin/clientes` (perfil **ADMIN**). A Web Blazor já está configurada para API localhost:5102 no arquivo `Merkdito.Web/appsettings.json` de laboratório.

Para o **site comercial**, abra a pasta `Frontend` no VS Code e sirva pelo Live Server na porta **5500**. Entre em `http://localhost:5500/index.html` com usuário **CLIENTE** de uma empresa. `Frontend/assets/js/config.js` aponta para a mesma API localhost:5102.

No site comercial, crie pelo menos um PDV de venda em `lojas.html`, cadastre os produtos e associe saldo e preço àquele PDV em `estoque_loja.html` (não basta cadastrar o produto no estoque central). Consulte a lista em `produtos.html` e `vendas.html`.

O novo painel `http://localhost:5500/compras_app.html` mostra, autenticado na conta CLIENTE, somente compras com origem `APP_CONSUMIDOR`. `REGISTRADA` não comprova pagamento.

## 4. Testar leitura do site para o app pela API

No PowerShell, execute:

```powershell
cd C:\MerkditoIntegrado
.\scripts\TESTAR_INTEGRACAO_SITE_APP.ps1 -EmailEmpresa "empresa@exemplo.invalid"
```

O script pede a **senha da empresa** no prompt (sem gravar no código), faz login CLIENTE, lista seus PDVs, cadastra um consumidor de testes, valida o login, vínculos e compara **produto, preço e estoque** da API administrativa com a API consumidor. O script não cria venda sem a opção explícita abaixo. Seu banco deve já ter produtos vinculados aos PDVs.

## 5. Testar a venda no banco de DEMONSTRAÇÃO

Ative a opção **apenas no user-secrets do ambiente isolado**:

```powershell
cd C:\MerkditoIntegrado\Merkdito.Api
dotnet user-secrets set "ComprasAutonomas:PermitirVendaSemPagamento" "true"
# Reinicie a API para a configuração de demonstração ser aplicada.
cd ..
.\scripts\TESTAR_INTEGRACAO_SITE_APP.ps1 -EmailEmpresa "empresa@exemplo.invalid" -RegistrarVendaTeste
```

Isso **reduz estoque real do banco de laboratório em uma unidade** e cria uma venda `REGISTRADA` sem pagamento. O teste verifica idempotência, saldo, histórico do consumidor, consulta da venda no site e os itens da compra. É proibido usar a flag em produção.

**Após a demonstração**, desligue a opção:

```powershell
cd C:\MerkditoIntegrado\Merkdito.Api
dotnet user-secrets set "ComprasAutonomas:PermitirVendaSemPagamento" "false"
```

## 6. Android físico

Use o celular por cabo USB com depuração habilitada. API deve executar no **mesmo PC**.

```powershell
adb devices
adb reverse tcp:5102 tcp:5102
cd C:\MerkditoIntegrado\app_flutter
flutter pub get
flutter analyze
flutter test
flutter run --dart-define=MERKDITO_API_URL=http://127.0.0.1:5102
```

`adb reverse` só funciona com dispositivo conectado e encaminhamento ativo. O APK de debug pode ser gerado com:

```powershell
flutter build apk --debug --dart-define=MERKDITO_API_URL=http://127.0.0.1:5102
```

O `.apk` estará em `app_flutter/build/app/outputs/flutter-apk/app-debug.apk`. Com essa URL o app só conecta à API enquanto o `adb reverse` estiver ativo. Para testar sem USB, configure API HTTPS acessível pelo celular e compile com `--dart-define=MERKDITO_API_URL=https://SEU_HOST_DE_TESTE`.

## 7. Critérios mínimos para aceite

- Compilar .NET (`Merkdito.sln`) e Flutter sem falhas.
- Criar empresa/PDV no site; visualizar o mesmo PDV no cadastro do app.
- Cadastrar consumidor com múltiplos vínculos, sair e logar novamente, reiniciar app e validar sessão armazenada.
- Produto ativo do PDV aparece no app com o mesmo preço/estoque do site; produto de outro PDV não aparece.
- Confirmar compra **fictícia** no app; consultar no site como `APP_CONSUMIDOR`/`REGISTRADA`, e validar baixa e trilha de estoque.
- Bloquear consulta de PDV não vinculado, quantidade superior ao saldo e duplicidade de confirmação.
- Desligar flag e confirmar que API **bloqueia** venda sem pagamento.
- Para produção comercial, integrar provedor de pagamento com confirmação segura de webhook, revalidar estoques e só então considerar retirada liberada.

## Estado desta entrega

**Código-fonte preparado e verificado estaticamente; API/SQL/Android físico não foram executados nesta sessão.** Não afirmar homologação antes de rodar os testes acima e registrar logs.
