import 'dart:math';
import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import '../models/consumer_models.dart';
import '../services/merkdito_api.dart';

/// A sessão pertence ao consumidor; a loja é escolhida separadamente e
/// precisa estar entre os vínculos autorizados pela API.
class MerkditoState extends ChangeNotifier {
  final MerkditoApi api;
  final FlutterSecureStorage _storage;
  MerkditoState(this.api, {FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage();

  bool inicializado = false;
  bool autenticando = false;
  bool carregandoCatalogo = false;
  bool carregandoPedidos = false;
  bool finalizando = false;
  String? aviso;
  String? token;
  String nome = '';
  String email = '';
  int? estabelecimentoId;
  List<int> estabelecimentoIds = [];
  List<Loja> lojas = [];
  List<Produto> produtos = [];
  List<Pedido> pedidos = [];
  final Map<int, int> carrinho = {};

  bool get autenticado => token != null;
  bool get lojaEscolhida => autenticado && estabelecimentoId != null;
  List<Loja> get lojasVinculadas => lojas
      .where((x) => estabelecimentoIds.contains(x.id)).toList();
  Loja? get lojaAtual {
    for (final loja in lojasVinculadas) {
      if (loja.id == estabelecimentoId) return loja;
    }
    return null;
  }
  int get quantidadeCarrinho => carrinho.values.fold(0, (a, b) => a + b);
  double get estimativaCarrinho => produtos.fold(0.0,
      (total, p) => total + p.preco * (carrinho[p.id] ?? 0));

  Future<void> iniciar() async {
    try {
      final servidorSalvo = await _storage.read(key: 'consumer_api_base_url');
      if (servidorSalvo != null && servidorSalvo.isNotEmpty) {
        try {
          api.definirServidor(servidorSalvo);
        } on ApiException {
          await _storage.delete(key: 'consumer_api_base_url');
        }
      }
      final t = await _storage.read(key: 'consumer_token');
      if (t != null && t.isNotEmpty) {
        final perfil = await api.obterPerfil(t);
        token = t;
        nome = perfil['nome']?.toString() ?? '';
        email = perfil['email']?.toString() ?? '';
        estabelecimentoIds = (perfil['estabelecimentoIds'] as List<dynamic>? ?? [])
            .map((e) => (e as num).toInt()).toList();
        final idAnterior = int.tryParse(await _storage.read(key: 'consumer_store') ?? '');
        if (idAnterior != null && estabelecimentoIds.contains(idAnterior)) {
          estabelecimentoId = idAnterior;
        } else {
          await _storage.delete(key: 'consumer_store');
        }
        await atualizarDados();
      }
    } on ApiException catch (e) {
      if (e.codigo == 401 || e.codigo == 403) {
        await sair();
      } else {
        aviso = e.mensagem;
      }
    } catch (_) {
      aviso = 'Não foi possível recuperar a sessão. Verifique sua conexão.';
    } finally {
      inicializado = true;
      notifyListeners();
    }
  }

  // Na troca de servidor, limpa sessão antiga para não enviar JWT de um banco a outro.
  Future<void> configurarServidor(String endereco) async {
    final novo = MerkditoApi.normalizarEndereco(endereco);
    await api.testarConexao(novo);
    if (api.baseUrl != novo) {
      await sair();
      api.definirServidor(novo);
      lojas.clear();
      aviso = null;
      await _storage.write(key: 'consumer_api_base_url', value: novo);
      notifyListeners();
    }
  }

  Future<void> buscarLojas() async {
    lojas = await api.listarLojas();
    notifyListeners();
  }

  Future<void> entrar(String email, String senha) async {
    if (autenticando) return;
    autenticando = true; aviso = null; notifyListeners();
    try {
      final sessao = await api.entrar(email.trim(), senha);
      await _adotar(sessao);
    } finally {
      autenticando = false; notifyListeners();
    }
  }

  Future<void> cadastrar(String nome, String email, String senha, List<int> lojaIds) async {
    if (autenticando) return;
    autenticando = true; aviso = null; notifyListeners();
    try {
      final sessao = await api.cadastrar(nome.trim(), email.trim(), senha, lojaIds);
      await _adotar(sessao);
    } finally {
      autenticando = false; notifyListeners();
    }
  }

  Future<void> _adotar(Sessao sessao) async {
    token = sessao.token;
    nome = sessao.nome;
    email = sessao.email;
    estabelecimentoIds = sessao.estabelecimentoIds;
    estabelecimentoId = null; // o cliente escolhe um PDV depois do login
    carrinho.clear(); produtos.clear(); pedidos.clear();
    await _storage.write(key: 'consumer_token', value: token);
    await _storage.delete(key: 'consumer_store');
    await _storage.delete(key: 'checkout_key');
    await _storage.delete(key: 'checkout_cart');
    await atualizarDados();
  }

  Future<void> selecionarLoja(int id) async {
    if (!autenticado || !estabelecimentoIds.contains(id)) {
      throw const ApiException('Você não está vinculado a este PDV.', 403);
    }
    if (estabelecimentoId != id) {
      // impede reutilizar carrinho e chave idempotente de outra loja
      carrinho.clear(); produtos.clear();
      await _storage.delete(key: 'checkout_key');
      await _storage.delete(key: 'checkout_cart');
    }
    estabelecimentoId = id;
    await _storage.write(key: 'consumer_store', value: '$id');
    notifyListeners();
    await carregarCatalogo();
    await carregarHistorico();
  }

  Future<void> escolherOutroPdv() async {
    estabelecimentoId = null;
    produtos.clear(); carrinho.clear();
    await _storage.delete(key: 'consumer_store');
    await _storage.delete(key: 'checkout_key');
    await _storage.delete(key: 'checkout_cart');
    notifyListeners();
  }

  Future<void> atualizarVinculos(List<int> lojaIds) async {
    if (!autenticado) throw const ApiException('Entre na sua conta.', 401);
    estabelecimentoIds = await api.atualizarVinculos(token!, lojaIds);
    if (estabelecimentoId != null && !estabelecimentoIds.contains(estabelecimentoId)) {
      await escolherOutroPdv();
    }
    notifyListeners();
  }

  Future<void> atualizarDados() async {
    try { await buscarLojas(); } on ApiException catch (e) { aviso = e.mensagem; }
    if (lojaEscolhida) await carregarCatalogo();
    if (autenticado) await carregarHistorico();
  }

  Future<void> carregarCatalogo() async {
    if (!lojaEscolhida) return;
    carregandoCatalogo = true; aviso = null; notifyListeners();
    final lojaSolicitada = estabelecimentoId!;
    try {
      final resultado = await api.listarProdutos(lojaSolicitada, token!);
      if (estabelecimentoId != lojaSolicitada) return;
      produtos = resultado;
      final disponiveis = {for (final p in produtos) p.id: p.estoque};
      carrinho.removeWhere((id, qtd) => !disponiveis.containsKey(id));
      for (final id in carrinho.keys.toList()) {
        if (carrinho[id]! > disponiveis[id]!) carrinho[id] = disponiveis[id]!;
      }
    } on ApiException catch (e) {
      aviso = e.mensagem;
      if (e.codigo == 401) await sair();
      if (e.codigo == 403) await escolherOutroPdv();
    } finally {
      carregandoCatalogo = false; notifyListeners();
    }
  }

  Future<void> carregarHistorico() async {
    if (!autenticado) return;
    carregandoPedidos = true; notifyListeners();
    try { pedidos = await api.listarPedidos(token!); }
    on ApiException catch (e) {
      aviso = e.mensagem;
      if (e.codigo == 401) await sair();
    } finally {
      carregandoPedidos = false; notifyListeners();
    }
  }

  void alterarQuantidade(Produto produto, int quantidade) {
    if (quantidade <= 0) carrinho.remove(produto.id);
    else carrinho[produto.id] = quantidade > produto.estoque ? produto.estoque : quantidade;
    notifyListeners();
  }

  Future<Pedido> finalizarVenda() async {
    if (finalizando) throw const ApiException('Já existe uma confirmação em andamento.', 409);
    if (!lojaEscolhida || carrinho.isEmpty) {
      throw const ApiException('Escolha um PDV e adicione produtos à sacola.', 400);
    }
    finalizando = true; aviso = null; notifyListeners();
    try {
      final itens = Map<int, int>.from(carrinho);
      final assinatura = '${estabelecimentoId!}|' +
          (itens.entries.toList()..sort((a, b) => a.key.compareTo(b.key)))
              .map((e) => '${e.key}:${e.value}').join(';');
      var chave = await _storage.read(key: 'checkout_key');
      final salva = await _storage.read(key: 'checkout_cart');
      if (chave == null || salva != assinatura) {
        chave = _criarChaveAleatoria();
        await _storage.write(key: 'checkout_key', value: chave);
        await _storage.write(key: 'checkout_cart', value: assinatura);
      }
      final pedido = await api.registrarVenda(token!, estabelecimentoId!, chave, itens);
      carrinho.clear();
      await _storage.delete(key: 'checkout_key');
      await _storage.delete(key: 'checkout_cart');
      await carregarCatalogo();
      await carregarHistorico();
      return pedido;
    } on ApiException catch (e) {
      if (e.codigo == 401) await sair();
      rethrow;
    } finally {
      finalizando = false; notifyListeners();
    }
  }

  static String _criarChaveAleatoria() {
    final rand = Random.secure();
    final bytes = List<int>.generate(16, (_) => rand.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
        '${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
  }

  Future<void> sair() async {
    token = null; estabelecimentoId = null; nome = ''; email = '';
    estabelecimentoIds = [];
    produtos = []; pedidos = []; carrinho.clear(); aviso = null;
    await _storage.delete(key: 'consumer_token');
    await _storage.delete(key: 'consumer_store');
    await _storage.delete(key: 'checkout_key');
    await _storage.delete(key: 'checkout_cart');
    notifyListeners();
  }

  @override void dispose() { api.dispose(); super.dispose(); }
}
