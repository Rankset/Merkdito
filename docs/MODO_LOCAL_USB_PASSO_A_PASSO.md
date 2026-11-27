# MERKDITO — integração local site + API + SQL Server + Android USB

**Versão de laboratório 1.4 (código-fonte)** — preparação para o PIM, baseada nos **dois ZIPs originais reenviados** e nos ajustes da integração v1.3. Não depende de hospedagem ou API pública.

## 1. O que há nos arquivos originais

- `merkdito_finalizado(1).zip`: código-fonte .NET, modelos, controllers, páginas Blazor, HTML/CSS/JS do site; o ZIP achatou os diretórios. Há DLLs de compilação antigas, mas **não há um arquivo de banco `.mdf`, `.bak` ou um SQL Server pronto** no ZIP. A pasta `Merkdito.Api` deste pacote é uma **reorganização do site original** somada à nova API de consumidor.
- `app_pim_4_final (1).zip`: aplicativo Flutter visual de protótipo, com catálogo e pedidos locais em memória; não havia comunicação com SQL Server por API no código original. O `app_flutter` deste pacote contém a implementação de integração pelo HTTP e a interface simplificada do consumidor. **Não é idêntica à interface original de demonstração**.
- `sql/00_MerkditoDb_original.sql` foi montado com base nas migrations fornecidas e cria a estrutura original de um **banco NOVO**. Scripts `01` e `02` criam as tabelas adicionais de consumidor/vínculos. **Nunca rode os scripts de criação num banco que já está em uso sem analisar o esquema, fazer backup e planejar a atualização**.

## 2. Esquema: tudo no mesmo notebook

```text
Notebook Windows (ambiente PIM)
  SQL Server Express  localhost\\SQLEXPRESS  / MerkditoDb
              ↑
  API .NET 10        http://localhost:5102    [única com acesso ao SQL]
              ↑                           ↑
  Site HTML          http://localhost:5500     App Flutter
  Painel Blazor      http://localhost:5254     ↕ cabo USB / ADB reverse
                                         Android físico
```

**O telefone não acessa o SQL Server.** O aplicativo usa as rotas da API (consumidores, empresas, PDVs, catálogo e compras). `adb reverse tcp:5102 tcp:5102` faz o endereço `127.0.0.1:5102` do celular chegar à API que está no notebook. Isso funciona mesmo que o celular esteja em 4G/5G; o **cabo USB e a depuração precisam continuar ativos**.

## 3. Pré-requisitos no Windows

1. Instalar **.NET 10 SDK**, **SQL Server Express**, **SQL Server Management Studio (SSMS)**, **Flutter SDK**, **Git**, **Android Studio/Android SDK Platform Tools**.
2. Ativar **Opções do desenvolvedor > Depuração USB** no Android e aceitar o computador no diálogo do aparelho.
3. Extrair **a pasta interna `MerkditoIntegrado`** do ZIP em `C:\\MerkditoIntegrado`, de modo que `C:\\MerkditoIntegrado\\Merkdito.sln` exista.
4. Abrir PowerShell e rodar:

```powershell
Set-Location C:\MerkditoIntegrado
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\01_DIAGNOSTICO.ps1
```

Esse primeiro script **não altera nada**. Confira se mostra .NET, Flutter, ADB e serviço SQL Server. Se faltar algum, instale-o antes de avançar.

## 4. Banco de dados — conferir ANTES de criar

No SSMS, conectar a `localhost\SQLEXPRESS` com **Windows Authentication**.

Primeiro execute `sql/DIAGNOSTICO_BANCO_EXISTENTE_SOMENTE_LEITURA.sql`. Ele não faz alteração.

- Se o banco **MerkditoDb não existe**: execute, nesta ordem e **uma única vez** no SSMS: `sql/00_MerkditoDb_original.sql`, `sql/01_Merkdito_Consumidor_App.sql`, `sql/02_Merkdito_Vinculos_PDV.sql`. Cada um deve terminar sem erro.
- Se o banco **já existe** (por exemplo, no notebook do colega): **NÃO rode `00`**. Verifique as tabelas e migrations do banco já existente, faça backup e avalie quais tabelas complementares precisam ser criadas. Não execute `01/02` sem verificar a compatibilidade real. Assegure-se de que site e API usam **exatamente o mesmo banco**.

## 5. Configurar, iniciar e verificar a API local

No PowerShell:

```powershell
Set-Location C:\MerkditoIntegrado
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\02_CONFIGURAR_API_LAB.ps1
# Se a instância SQL tiver OUTRO NOME, use -InstanciaSql 'SERVIDOR\INSTANCIA'
```

Esse script configura a conexão e, se ainda não existir, gera uma chave JWT para **User Secrets do .NET**. Não coloca senhas no GitHub. O modo de venda fictícia fica desligado.

Abra **outra janela** do PowerShell, execute e mantenha aberta:

```powershell
Set-Location C:\MerkditoIntegrado
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\03_INICIAR_API_LOCAL.ps1
```

Abra **mais uma janela** e execute:

```powershell
Set-Location C:\MerkditoIntegrado
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\04_TESTAR_API_E_BANCO.ps1
```

A verificação faz **duas consultas distintas**: `GET /healthz` confirma que a API está respondendo; `GET /api/consumidor/lojas` confirma que a API consegue consultar o **SQL Server**. Se retornar zero lojas, é possível que não exista ainda um PDV ativo cadastrado.

## 6. Abrir o site no mesmo notebook

Site comercial HTML (nesta versão o servidor local é uma pequena aplicação ASP.NET, **sem precisar instalar Live Server**):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\05_INICIAR_SITE_LOCAL.ps1
```

Abrir `http://localhost:5500/index.html` (e deixar o terminal aberto). O site aponta para a API `http://localhost:5102/api` e essa origem já está autorizada na configuração local de CORS. O site e o app usam a **mesma API e o mesmo banco**, não duas listas de produtos distintas.

Painel geral Blazor (opcional em uma quarta janela):

```powershell
dotnet run --project .\Merkdito.Web --launch-profile http
```

O painel costuma ouvir em `http://localhost:5254`. O banco novo **não vem com conta ADMIN inicial**: para gerá-la, use o gerador interativo `dotnet run --project .\scripts\GERAR_HASH_ADMIN.csproj` e salve o hash, e-mail e configurações `AdminAuth` nos User Secrets conforme documentação de `docs/COMO_TESTAR_SITE_E_APP.md`. Não publique a senha do administrador.

No site, cadastre primeiro uma empresa/cliente e **uma loja/PDV ativo**. Cadastre produtos e associe cada produto ao PDV com **preço e estoque específicos**, usando as telas do site. Produto sem vínculo ao PDV não aparecerá no app.

## 7. Rodar o app direto no Android por USB (sem APK distribuível)

Conecte o Android por um **cabo que transmite dados**, mantenha a depuração USB ligada e autorize o computador. Com API e SQL Server rodando, execute:

```powershell
Set-Location C:\MerkditoIntegrado
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\06_EXECUTAR_APP_USB.ps1
```

O script:

1. Verifica que `/healthz` está ativo no notebook.
2. Identifica o Android físico com ADB.
3. Configura `adb reverse tcp:5102 tcp:5102` **antes de abrir o app**.
4. Executa `flutter pub get`, `flutter analyze --no-fatal-infos`, `flutter test`.
5. Executa `flutter run -d ID --dart-define=MERKDITO_API_URL=http://127.0.0.1:5102`.

**Não feche a janela da API, não desconecte o cabo e não feche o terminal do Flutter.** O app depende deles nesta arquitetura local. Caso haja mais de um aparelho, informe `-DispositivoId <ID_DO_ADB>`.

Teste simples antes de abrir o app: no Chrome do **celular conectado com ADB reverse**, abra `http://127.0.0.1:5102/healthz`. O esperado é `{"status":"ok"}`. Se falhar, não adianta tentar cadastrar no app; corrija o servidor/USB primeiro.

Se você estiver utilizando um APK já instalado com o botão **Configurar conexão**, informe `http://127.0.0.1:5102` depois de estabelecer o `adb reverse`. **Não use um endereço público inventado.**

## 8. Comprovar que app e site estão de fato integrados

- No site, crie empresa, PDV ativo, produto, preço e estoque por PDV.
- No Android, cadastro deve listar as mesmas empresas/PDVs; vincule pelo menos um.
- Saia e entre novamente: a conta e os vínculos devem persistir no banco.
- Escolha o PDV e confira nome/preço/estoque dos mesmos produtos do site.
- Troque de PDV: a sacola anterior deve ser limpa e o catálogo trocado.
- Execute o teste integrado **de leitura e cadastro fictício** (cria uma conta de teste no banco do laboratório):

```powershell
.\scripts\TESTAR_INTEGRACAO_SITE_APP.ps1 -EmailEmpresa 'EMAIL_DA_EMPRESA'
```

Esse teste exige uma empresa e produto já cadastrados pelo site e a senha da conta da empresa, solicitada no prompt. Ele valida o contrato da API, mas **não altera o estoque** por padrão.

**Finalização de compra:** o sistema não possui confirmação de pagamento integrada. O modo de venda de demonstração pode registrar uma venda como `REGISTRADA` e diminuir o estoque, mas **NÃO** deve ser ativado em produção nem considerado pagamento aprovado. Só usar em banco totalmente fictício e com autorização da equipe. Consulte `docs/COMO_TESTAR_SITE_E_APP.md` para ativar/desativar esse teste explicitamente.

## 9. Só depois: gerar APK de laboratório

Quando API, SQL e Android USB estiverem aprovados:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\07_GERAR_APK_LAB_USB.ps1
```

O script confere API local, analisa/testa Flutter e gera APK debug com endereço `127.0.0.1`. Ele **ainda depende de ADB reverse** para operar na arquitetura proposta. O APK não inclui nem instala SQL Server ou API; portanto, sozinho, sem notebook rodando, não conseguirá realizar cadastro nem consultar produtos.

## 10. O que já foi verificado e o que continua pendente

- Identidade dos dois ZIPs originais: SHA-256 comparados com os pacotes já disponíveis. Os ZIPs são os mesmos códigos reenviados.
- Estrutura funcional site/API/app da v1.3 verificada por 59 checagens estáticas.
- Rotas, configurações de localhost, manifest Android debug e Gradle modernizado verificados por 20 checagens estáticas adicionais.
- Histórico do projeto registra compilação .NET e Android debug no GitHub Actions, mas isso **não substitui** testes reais com SQL Server local, sessões e venda no notebook do grupo.
- Esta nova revisão de código e scripts locais **não foi compilada/executada neste container** porque não há .NET, Flutter, SQL Server e ADB instalados. Sua homologação precisa usar o notebook da equipe.

**Critério de aceite:** mostrar no celular uma empresa/PDV criada no site, o produto com preço/estoque igual, e uma venda fictícia (se autorizada) aparecendo no site e alterando o mesmo estoque no banco. Só então considerar o fluxo local integrado homologado para a demonstração do PIM.
