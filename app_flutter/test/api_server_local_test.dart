import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:merkdito_app/services/merkdito_api.dart';

void main() {
  test('Servidor HTTP local por USB é aceito', () {
    expect(MerkditoApi.normalizarEndereco('http://127.0.0.1:5102/'), 'http://127.0.0.1:5102');
    expect(MerkditoApi.normalizarEndereco('http://192.168.0.20:5102'), 'http://192.168.0.20:5102');
  });
  test('Servidor público não pode usar HTTP', () {
    expect(() => MerkditoApi.normalizarEndereco('http://api.exemplo.org'), throwsA(isA<ApiException>()));
    expect(MerkditoApi.normalizarEndereco('https://api.exemplo.org/'), 'https://api.exemplo.org');
  });
  test('Bloqueia URL com credenciais ou caminhos extras', () {
    for (final url in ['https://usr:pass@api.exemplo.org', 'https://api.exemplo.org/api', 'https://api.exemplo.org/?a=1']) {
      expect(() => MerkditoApi.normalizarEndereco(url), throwsA(isA<ApiException>()));
    }
  });
  test('Confirma API correta com healthz', () async {
    final api = MerkditoApi(client: MockClient((r) async => http.Response(jsonEncode({'status':'ok'}),200)));
    await api.testarConexao('http://127.0.0.1:5102');
    api.definirServidor('http://127.0.0.1:5102');
    expect(api.baseUrl, 'http://127.0.0.1:5102');
    api.dispose();
  });
  test('Não aceita página HTML no lugar da API', () async {
    final api = MerkditoApi(client: MockClient((r) async => http.Response('<html>Site</html>',200)));
    await expectLater(api.testarConexao('https://api.exemplo.org'), throwsA(isA<ApiException>()));
    api.dispose();
  });
}
