import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;
import '../models/consumer_models.dart';

class ApiException implements Exception {
  final String mensagem;
  final int codigo;
  const ApiException(this.mensagem, this.codigo);
  @override String toString() => mensagem;
}

/// A API é a única fonte de informações e a única responsável por finalizar vendas.
/// Exemplo Android emulador: --dart-define=MERKDITO_API_URL=http://10.0.2.2:5102
class MerkditoApi {
  static const enderecoPadrao = String.fromEnvironment('MERKDITO_API_URL', defaultValue: 'http://127.0.0.1:5102');
  String baseUrl;
  final http.Client _client;
  MerkditoApi({String? baseUrl, http.Client? client})
      : baseUrl = (baseUrl ?? enderecoPadrao).replaceFirst(RegExp(r'/$'), ''),
        _client = client ?? http.Client();


  // A API pode ser configurada sem recompilar. HTTP somente em rede local.
  static String normalizarEndereco(String endereco) {
    final valor = endereco.trim().replaceFirst(RegExp(r'/+$'), '');
    final uri = Uri.tryParse(valor);
    if (uri == null || uri.host.isEmpty || uri.userInfo.isNotEmpty ||
        uri.hasQuery || uri.hasFragment ||
        (uri.path.isNotEmpty && uri.path != '/')) {
      throw const ApiException('Informe o endereço base da API sem caminhos ou parâmetros.', 0);
    }
    final host = uri.host.toLowerCase();
    final privado = host == 'localhost' || host == '127.0.0.1' ||
        host.startsWith('192.168.') || host.startsWith('10.') ||
        RegExp(r'^172\.(1[6-9]|2[0-9]|3[01])\.').hasMatch(host);
    if (uri.scheme != 'https' && !(uri.scheme == 'http' && privado)) {
      throw const ApiException('HTTPS é obrigatório fora da rede local de laboratório.', 0);
    }
    return valor;
  }

  void definirServidor(String url) { baseUrl = normalizarEndereco(url); }

  Future<void> testarConexao(String endereco) async {
    final url = normalizarEndereco(endereco);
    try {
      final resposta = await _client.get(Uri.parse('$url/healthz'))
          .timeout(const Duration(seconds: 8));
      if (resposta.statusCode != 200) {
        throw ApiException('API retornou HTTP ${resposta.statusCode}. Confira o servidor.', resposta.statusCode);
      }
      final dados = jsonDecode(resposta.body);
      if (dados is! Map || dados['status'] != 'ok') {
        throw const ApiException('O servidor não confirmou /healthz do Merkdito.', 0);
      }
    } on ApiException {
      rethrow;
    } on TimeoutException {
      throw const ApiException('API demorou para responder. Confira cabo USB, adb reverse e serviço.', 0);
    } catch (_) {
      throw const ApiException('API inacessível. Inicie o servidor local e configure o adb reverse.', 0);
    }
  }

  Future<dynamic> _requisitar(String metodo, String caminho,
      {String? token, Map<String, dynamic>? dados}) async {
    if (baseUrl.isEmpty) {
      throw const ApiException('O endereço da API não foi configurado no aplicativo.', 0);
    }
    final uri = Uri.parse('$baseUrl$caminho');
    final req = http.Request(metodo, uri)
      ..headers['Accept'] = 'application/json'
      ..headers['Content-Type'] = 'application/json';
    if (token != null && token.isNotEmpty) req.headers['Authorization'] = 'Bearer $token';
    if (dados != null) req.body = jsonEncode(dados);
    late http.Response response;
    try {
      response = await _client.send(req)
          .then(http.Response.fromStream)
          .timeout(const Duration(seconds: 20));
    } on TimeoutException {
      throw const ApiException('O servidor demorou a responder. Verifique sua conexão.', 0);
    } catch (_) {
      throw ApiException('API inacessível em $baseUrl. Verifique USB/ADB e configure o servidor.', 0);
    }
    dynamic json;
    try { json = response.body.isEmpty ? null : jsonDecode(response.body); }
    on FormatException { json = null; }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      if (response.statusCode == 401) {
        throw const ApiException('Sessão expirada ou credenciais inválidas.', 401);
      }
      if (response.statusCode == 403) {
        throw const ApiException('Você não tem permissão para acessar esta loja.', 403);
      }
      if (response.statusCode == 429) {
        throw const ApiException('Muitas tentativas. Tente novamente mais tarde.', 429);
      }
      final mensagem = (json is Map ? json['mensagem'] : null) as String?;
      throw ApiException(mensagem ?? 'Não foi possível completar a solicitação (${response.statusCode}).',
          response.statusCode);
    }
    return json;
  }

  Future<List<Loja>> listarLojas() async {
    final dados = await _requisitar('GET', '/api/consumidor/lojas') as List;
    return dados.map((x) => Loja.fromJson(Map<String, dynamic>.from(x as Map))).toList();
  }

  Future<Sessao> cadastrar(String nome, String email, String senha, List<int> lojasIds) async {
    final dados = await _requisitar('POST', '/api/consumidor/cadastro', dados: {
      'nome': nome, 'email': email, 'senha': senha, 'estabelecimentoIds': lojasIds
    }) as Map;
    return Sessao.fromJson(Map<String, dynamic>.from(dados));
  }

  Future<Sessao> entrar(String email, String senha) async {
    final dados = await _requisitar('POST', '/api/consumidor/login', dados: {
      'email': email, 'senha': senha
    }) as Map;
    return Sessao.fromJson(Map<String, dynamic>.from(dados));
  }

  Future<Map<String, dynamic>> obterPerfil(String token) async {
    final dados = await _requisitar('GET', '/api/consumidor/perfil', token: token) as Map;
    return Map<String, dynamic>.from(dados);
  }

  Future<List<int>> atualizarVinculos(String token, List<int> lojaIds) async {
    final retorno = await _requisitar('PUT', '/api/consumidor/vinculos', token: token,
      dados: {'estabelecimentoIds': lojaIds}) as Map;
    return (retorno['estabelecimentoIds'] as List<dynamic>)
        .map((x) => (x as num).toInt()).toList();
  }

  Future<List<Produto>> listarProdutos(int lojaId, String token) async {
    final dados = await _requisitar('GET', '/api/consumidor/lojas/$lojaId/produtos', token: token) as List;
    return dados.map((x) => Produto.fromJson(Map<String, dynamic>.from(x as Map))).toList();
  }

  Future<List<Pedido>> listarPedidos(String token) async {
    final dados = await _requisitar('GET', '/api/consumidor/pedidos', token: token) as List;
    return dados.map((x) => Pedido.fromJson(Map<String, dynamic>.from(x as Map))).toList();
  }

  Future<Pedido> registrarVenda(String token, int lojaId, String chave, Map<int, int> itens) async {
    final dados = await _requisitar('POST', '/api/consumidor/pedidos', token: token, dados: {
      'estabelecimentoId': lojaId,
      'idempotencyKey': chave,
      'itens': itens.entries.map((e) => {'produtoId': e.key, 'quantidade': e.value}).toList(),
    }) as Map;
    return Pedido.fromJson(Map<String, dynamic>.from(dados));
  }

  void dispose() => _client.close();
}
