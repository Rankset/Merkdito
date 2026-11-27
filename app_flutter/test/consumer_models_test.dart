import 'package:flutter_test/flutter_test.dart';
import 'package:merkdito_app/models/consumer_models.dart';

void main() {
  test('preço numérico é convertido para valor monetário', () {
    final p = Produto.fromJson({
      'id': 1, 'nome': 'Água', 'descricao': '', 'categoriaId': 2,
      'preco': 5, 'estoque': 10,
    });
    expect(p.preco, 5.0);
    expect(p.estoque, 10);
  });

  test('sessão mantém mais de um PDV sem selecionar a loja automaticamente', () {
    final sessao = Sessao.fromJson({'token':'teste','nome':'João','email':'j@example.com','estabelecimentoIds':[4,9]});
    expect(sessao.estabelecimentoIds, [4,9]);
  });

  test('pedido sem itens admite resposta de confirmação da API', () {
    final p = Pedido.fromJson({'pedidoId': 44, 'total': 19.9, 'status': 'REGISTRADA'});
    expect(p.pedidoId, 44);
    expect(p.itens, isEmpty);
  });
}
