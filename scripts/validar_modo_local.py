#!/usr/bin/env python3
"""Validação estática isolada do modo USB (não substitui Android/SQL)."""
from pathlib import Path
import json
base = Path(__file__).resolve().parent.parent
verificacoes = {
 'site original': (base/'Frontend/index.html').is_file(),
 'API .NET 10': 'net10.0' in (base/'Merkdito.Api/Merkdito.Api.csproj').read_text(),
 'site estático localhost': 'http://localhost:5500' in (base/'scripts/site_local/Program.cs').read_text(),
 'frontend API local': 'http://localhost:5102/api' in (base/'Frontend/assets/js/config.js').read_text(),
 'API HTTP local': 'http://localhost:5102' in (base/'Merkdito.Api/Properties/launchSettings.json').read_text(),
 'CORS site local': 'http://localhost:5500' in (base/'Merkdito.Api/appsettings.json').read_text(),
 'aplicativo consulta API e não SQL direto': 'listarLojas' in (base/'app_flutter/lib/services/merkdito_api.dart').read_text(),
 'app tem INTERNET': 'android.permission.INTERNET' in (base/'app_flutter/android/app/src/main/AndroidManifest.xml').read_text(),
 'HTTP permitido só debug': 'usesCleartextTraffic="true"' in (base/'app_flutter/android/app/src/debug/AndroidManifest.xml').read_text(),
 'HTTP não permitido explicitamente em release': 'usesCleartextTraffic="true"' not in (base/'app_flutter/android/app/src/main/AndroidManifest.xml').read_text(),
 'Gradle 8.13': 'gradle-8.13-all.zip' in (base/'app_flutter/android/gradle/wrapper/gradle-wrapper.properties').read_text(),
 'AGP 8.13': 'com.android.application" version "8.13.0"' in (base/'app_flutter/android/settings.gradle').read_text(),
 'Kotlin 2.2.20': 'org.jetbrains.kotlin.android" version "2.2.20"' in (base/'app_flutter/android/settings.gradle').read_text(),
 'Android USB usa adb reverse': 'reverse tcp:5102 tcp:5102' in (base/'scripts/06_EXECUTAR_APP_USB.ps1').read_text(encoding='utf-8-sig'),
 'Android USB seleciona dispositivo': '-d $DispositivoId' in (base/'scripts/06_EXECUTAR_APP_USB.ps1').read_text(encoding='utf-8-sig'),
 'scripts SQL versionados': all((base/'sql'/s).exists() for s in ['00_MerkditoDb_original.sql','01_Merkdito_Consumidor_App.sql','02_Merkdito_Vinculos_PDV.sql']),
 'endpoint público lista PDVs': '[HttpGet("lojas")]' in (base/'Merkdito.Api/Controllers/ConsumidorController.cs').read_text(),
 'endpoint protegido cataloga por PDV': '[HttpGet("lojas/{lojaId:int}/produtos")]' in (base/'Merkdito.Api/Controllers/ConsumidorController.cs').read_text(),
 'API sem pagamento bloqueada por padrão': '"PermitirVendaSemPagamento": false' in (base/'Merkdito.Api/appsettings.json').read_text(),
 'teste nunca ativa modo fictício': 'PermitirVendaSemPagamento' not in (base/'scripts/04_TESTAR_API_E_BANCO.ps1').read_text(encoding='utf-8-sig'),
}
print(json.dumps({'total':len(verificacoes),'aprovadas':sum(verificacoes.values()),'erros':[k for k,v in verificacoes.items() if not v]},ensure_ascii=False,indent=2))
raise SystemExit(0 if all(verificacoes.values()) else 1)
