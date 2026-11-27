#requires -Version 5.1
<#
  Merkdito - modo apresentacao local (sem nuvem, sem banco paralelo).
  Uma janela inicializa API/site se necessario, monitora USB e restabelece adb reverse.
  NUNCA cria/atualiza banco nem habilita compras sem pagamento.
#>
param(
  [string]$DispositivoId = '',
  [int]$IntervaloSegundos = 5,
  [switch]$SemSite,
  [switch]$NaoAbrirApp,
  [string]$ArquivoApk = ''
)
$ErrorActionPreference = 'Stop'
$base = Split-Path -Parent $PSScriptRoot
$api = 'http://127.0.0.1:5102'
$pacote = 'com.merkdito.merkdito_app'
$instalador = if ($ArquivoApk) { $ArquivoApk } else { Join-Path $base 'APK\Merkdito_Android_USB_PIM.apk' }
if ($IntervaloSegundos -lt 3 -or $IntervaloSegundos -gt 60) { throw 'IntervaloSegundos deve estar entre 3 e 60.' }

function Aviso([string]$mensagem) { Write-Host "[MERKDITO] $mensagem" -ForegroundColor Cyan }
function SaudeApi {
  try {
    $resposta = Invoke-RestMethod "$api/healthz" -TimeoutSec 3
    return ($resposta.status -eq 'ok')
  } catch { return $false }
}
function LerLojas {
  try {
    # Healthz OK sozinho nao verifica o SQL Server!
    $resultado = @(Invoke-RestMethod "$api/api/consumidor/lojas" -TimeoutSec 15)
    Aviso "API conectada ao banco. PDVs ativos retornados: $($resultado.Count)."
    if ($resultado.Count -eq 0) {
      Write-Warning 'Nenhum PDV ativo retornado. Confirme cadastro de empresas, lojas e estoques no site.'
    }
    return $true
  } catch {
    Write-Warning "A API iniciou, mas NAO conseguiu consultar o banco SQL Server: $($_.Exception.Message)"
    return $false
  }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw 'Instale .NET SDK 10 e configure SQL Server antes de usar este inicializador.'
}
$adbComando = Get-Command adb -ErrorAction SilentlyContinue
$adb = if ($adbComando) { $adbComando.Source } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe' }
if (-not (Test-Path $adb)) { throw 'adb.exe nao encontrado. Instale Android SDK Platform-Tools e habilite depuracao USB.' }
if (-not (Test-Path (Join-Path $base 'Merkdito.Api\Merkdito.Api.csproj'))) {
  throw 'Nao localizei a API. Extraia a pasta MerkditoIntegrado completa.'
}

Aviso 'Iniciando ambiente de apresentacao (SQL Server local + API local + Android USB)'
if (-not (SaudeApi)) {
  Aviso 'API nao esta executando. Abrindo terminal da API local...'
  $scriptApi = Join-Path $PSScriptRoot '03_INICIAR_API_LOCAL.ps1'
  Start-Process -FilePath 'powershell.exe' -WorkingDirectory $base -ArgumentList @(
    '-NoProfile','-NoExit','-ExecutionPolicy','Bypass','-File',('"' + $scriptApi + '"')) | Out-Null
  $ate = (Get-Date).AddSeconds(75)
  while ((Get-Date) -lt $ate -and -not (SaudeApi)) { Start-Sleep -Seconds 2 }
  if (-not (SaudeApi)) {
    throw 'A API nao iniciou na porta 5102. Confira a janela da API, SQL Server, User Secrets e scripts do banco.'
  }
}
Aviso 'API: http://127.0.0.1:5102/healthz OK'
if (-not (LerLojas)) { throw 'Banco nao validado. Corrija a conexao SQL/estrutura antes de conectar o celular.' }

if (-not $SemSite) {
  $siteAtivo = $false
  try { $r = Invoke-WebRequest 'http://127.0.0.1:5500/index.html' -UseBasicParsing -TimeoutSec 3; $siteAtivo = $r.StatusCode -eq 200 } catch { }
  if (-not $siteAtivo) {
    Aviso 'Abrindo o site HTML local no endereco http://localhost:5500/index.html'
    $siteScript = Join-Path $PSScriptRoot '05_INICIAR_SITE_LOCAL.ps1'
    Start-Process -FilePath 'powershell.exe' -WorkingDirectory $base -ArgumentList @(
      '-NoProfile','-NoExit','-ExecutionPolicy','Bypass','-File',('"' + $siteScript + '"')) | Out-Null
  } else { Aviso 'O site local ja esta ativo na porta 5500.' }
}

& $adb start-server | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel iniciar o servico ADB.' }
Aviso 'Monitor USB iniciado. Conecte o celular e confirme a autorizacao de depuracao.'
Aviso 'Se o app guardou outro servidor, configure UMA VEZ http://127.0.0.1:5102 no menu do app.'
Aviso 'Mantenha esta janela aberta durante a apresentacao. CTRL+C encerra o monitor USB.'

$conhecidos = @{}
while ($true) {
  $linhas = @(& $adb devices 2>$null)
  $autorizados = @($linhas | Where-Object { $_ -match '^([^\s]+)\s+device\s*$' } | ForEach-Object { ($_ -split '\s+')[0] })
  if ($DispositivoId) { $autorizados = @($autorizados | Where-Object { $_ -eq $DispositivoId }) }
  $naoAutorizados = @($linhas | Where-Object { $_ -match '\s+unauthorized\s*$' })
  if ($naoAutorizados.Count -gt 0 -and $conhecidos.Count -eq 0) {
    Write-Host 'Desbloqueie o celular e aceite "Permitir depuracao USB".' -ForegroundColor Yellow
  }
  foreach ($serial in $autorizados) {
    $mapeamentos = @(& $adb -s $serial reverse --list 2>$null)
    $possuiReverso = @($mapeamentos | Where-Object { $_ -match 'tcp:5102\s+tcp:5102' }).Count -gt 0
    if (-not $possuiReverso) {
      & $adb -s $serial reverse 'tcp:5102' 'tcp:5102' | Out-Null
      if ($LASTEXITCODE -ne 0) { Write-Warning "Falha adb reverse no aparelho $serial"; continue }
      Aviso "Conexao USB para a API reestabelecida: $serial -> $api"
    }
    if (-not $conhecidos.ContainsKey($serial)) {
      $conhecidos[$serial] = $true
      Aviso "Celular reconhecido e encaminhamento USB ativo: $serial"
      $instalado = @(& $adb -s $serial shell pm path $pacote 2>$null)
      if ($instalado.Count -eq 0 -and (Test-Path $instalador)) {
        Aviso "Instalando APK fornecido: $([IO.Path]::GetFileName($instalador))"
        & $adb -s $serial install -r $instalador
        if ($LASTEXITCODE -ne 0) {
          Write-Warning 'Instalacao falhou. Se a assinatura mudou, desinstale a versao antiga manualmente (isso apaga dados locais) e tente novamente.'
        }
      }
      if (-not $NaoAbrirApp) {
        $instalado = @(& $adb -s $serial shell pm path $pacote 2>$null)
        if ($instalado.Count -gt 0) {
          & $adb -s $serial shell monkey -p $pacote -c android.intent.category.LAUNCHER 1 | Out-Null
          Aviso 'App aberto. Empresas e PDVs serao atualizados pela API local.'
        } else { Write-Warning 'APK nao encontrado no celular. Coloque o APK na pasta APK do pacote e reconecte, ou instale manualmente.' }
      }
    }
  }
  foreach ($id in @($conhecidos.Keys)) {
    if ($id -notin $autorizados) {
      $conhecidos.Remove($id)
      Aviso "USB desconectado ($id). O monitor religara a API quando reconectar."
    }
  }
  Start-Sleep -Seconds $IntervaloSegundos
}
