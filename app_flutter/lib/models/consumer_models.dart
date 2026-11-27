class Loja {
  final int id;
  final int empresaId;
  final String nome;
  final String empresa;
  const Loja({required this.id, required this.empresaId, required this.nome, required this.empresa});
  factory Loja.fromJson(Map<String, dynamic> j) => Loja(
    id: (j['id'] as num).toInt(),
    empresaId: (j['empresaId'] as num).toInt(),
    nome: j['nome'] as String? ?? '',
    empresa: j['empresa'] as String? ?? '',
  );
}

class Produto {
  final int id;
  final String nome;
  final String? descricao;
  final int categoriaId;
  final String categoriaNome;
  final double preco;
  final int estoque;
  final String? fotoUrl;
  const Produto({required this.id, required this.nome, required this.descricao,
    required this.categoriaId, required this.categoriaNome, required this.preco, required this.estoque, this.fotoUrl});
  factory Produto.fromJson(Map<String, dynamic> j) => Produto(
    id: (j['id'] as num).toInt(),
    nome: j['nome'] as String? ?? '',
    descricao: j['descricao'] as String?,
    categoriaId: (j['categoriaId'] as num).toInt(),
    categoriaNome: j['categoriaNome'] as String? ?? 'Categoria',
    preco: (j['preco'] as num).toDouble(),
    estoque: (j['estoque'] as num).toInt(),
    fotoUrl: j['fotoUrl'] as String?,
  );
}

class Sessao {
  final String token;
  final String nome;
  final String email;
  final List<int> estabelecimentoIds;
  final DateTime? expiraEm;
  Sessao({required this.token, required this.nome, required this.email,
    required this.estabelecimentoIds, this.expiraEm});
  factory Sessao.fromJson(Map<String, dynamic> j) => Sessao(
    token: j['token'] as String,
    nome: j['nome'] as String? ?? '',
    email: j['email'] as String? ?? '',
    estabelecimentoIds: (j['estabelecimentoIds'] as List<dynamic>? ?? [])
        .map((x) => (x as num).toInt()).toList(),
    expiraEm: DateTime.tryParse(j['expiresAtUtc']?.toString() ?? ''),
  );
}

class ItemPedido {
  final int produtoId;
  final String nome;
  final int quantidade;
  final double precoUnitario;
  const ItemPedido({required this.produtoId, required this.nome,
    required this.quantidade, required this.precoUnitario});
  factory ItemPedido.fromJson(Map<String, dynamic> j) => ItemPedido(
    produtoId: (j['produtoId'] as num).toInt(),
    nome: j['nome'] as String? ?? '',
    quantidade: (j['quantidade'] as num).toInt(),
    precoUnitario: (j['precoUnitario'] as num).toDouble(),
  );
}

class Pedido {
  final int pedidoId;
  final int? estabelecimentoId;
  final double total;
  final String status;
  final DateTime? criadoEm;
  final List<ItemPedido> itens;
  const Pedido({required this.pedidoId, this.estabelecimentoId, required this.total, required this.status,
    this.criadoEm, this.itens = const []});
  factory Pedido.fromJson(Map<String, dynamic> j) => Pedido(
    pedidoId: (j['pedidoId'] as num).toInt(),
    estabelecimentoId: (j['estabelecimentoId'] as num?)?.toInt(),
    total: (j['total'] as num).toDouble(),
    status: j['status'] as String? ?? 'REGISTRADA',
    criadoEm: DateTime.tryParse(j['criadoEmUtc']?.toString() ?? ''),
    itens: (j['itens'] as List<dynamic>? ?? [])
        .map((x) => ItemPedido.fromJson(Map<String, dynamic>.from(x as Map))).toList(),
  );
}
