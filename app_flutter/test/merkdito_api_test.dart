import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:merkdito_app/services/merkdito_api.dart';

void main() {
  test('lojas são interpretadas com empresa e estabelecimento', () async {
    final api = MerkditoApi(baseUrl: 'https://teste.example', client: MockClient((r) async {
      expect(r.url.path, '/api/consumidor/lojas');
      return http.Response(jsonEncode([
        {'id': 7, 'empresaId': 3, 'nome': 'Loja Centro', 'empresa': 'Empresa A'}
      ]), 200);
    }));
    final lojas = await api.listarLojas();
    expect(lojas.single.id, 7);
    expect(lojas.single.empresa, 'Empresa A');
    api.dispose();
  });

  test('a API recebe o token de consumidor para consultar estoque da loja', () async {
    final api = MerkditoApi(baseUrl: 'https://teste.example', client: MockClient((r) async {
      expect(r.headers['authorization'], 'Bearer token_teste');
      expect(r.url.path, '/api/consumidor/lojas/7/produtos');
      return http.Response(jsonEncode([
        {'id': 22, 'nome': 'Sabonete', 'descricao': null, 'categoriaId': 5,
         'preco': 3.5, 'estoque': 4}
      ]), 200);
    }));
    final itens = await api.listarProdutos(7, 'token_teste');
    expect(itens.single.preco, 3.5);
    expect(itens.single.estoque, 4);
    api.dispose();
  });

  test('uma venda usa a chave idempotente e não envia preços do aparelho', () async {
    final api = MerkditoApi(baseUrl: 'https://teste.example', client: MockClient((r) async {
      final corpo = jsonDecode(r.body) as Map<String, dynamic>;
      expect(corpo['idempotencyKey'], 'chave-de-teste');
      expect(corpo['estabelecimentoId'], 7);
      expect(corpo['itens'], [{'produtoId': 22, 'quantidade': 2}]);
      expect(r.body.contains('preco'), false);
      return http.Response('{"pedidoId":9,"total":7.0,"status":"REGISTRADA"}', 201);
    }));
    final pedido = await api.registrarVenda('token_teste', 7, 'chave-de-teste', {22: 2});
    expect(pedido.pedidoId, 9);
    expect(pedido.status, 'REGISTRADA');
    api.dispose();
  });

  test('cadastro vincula vários PDVs e recebe sessão do consumidor', () async {
    final api = MerkditoApi(baseUrl: 'https://teste.example', client: MockClient((r) async {
      expect(r.url.path, '/api/consumidor/cadastro');
      final corpo = jsonDecode(r.body) as Map<String, dynamic>;
      expect(corpo['estabelecimentoIds'], [7, 8]);
      expect(corpo.containsKey('estabelecimentoId'), false);
      return http.Response(jsonEncode({
        'token': 'jwt-teste', 'nome': 'Consumidor', 'email': 'teste@example.com',
        'estabelecimentoIds': [7, 8]
      }), 201);
    }));
    final sessao = await api.cadastrar('Consumidor', 'teste@example.com', 'senhadeteste123', [7, 8]);
    expect(sessao.estabelecimentoIds, [7, 8]);
    api.dispose();
  });

  test('edição dos vínculos usa autorização e método PUT', () async {
    final api = MerkditoApi(baseUrl: 'https://teste.example', client: MockClient((r) async {
      expect(r.method, 'PUT');
      expect(r.headers['authorization'], 'Bearer teste');
      expect(r.url.path, '/api/consumidor/vinculos');
      expect(jsonDecode(r.body)['estabelecimentoIds'], [8, 9]);
      return http.Response('{"estabelecimentoIds":[8,9]}', 200);
    }));
    expect(await api.atualizarVinculos('teste', [8, 9]), [8, 9]);
    api.dispose();
  });

  test('401 retorna mensagem de sessão expirada', () async {
    final api = MerkditoApi(baseUrl: 'https://teste.example', client: MockClient((r) async =>
      http.Response('{"mensagem":"falhou"}', 401)));
    await expectLater(api.listarPedidos('invalido'),
      throwsA(isA<ApiException>().having((x) => x.codigo, 'codigo', 401)));
    api.dispose();
  });
}
