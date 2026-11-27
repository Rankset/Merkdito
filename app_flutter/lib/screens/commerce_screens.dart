import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../models/consumer_models.dart';
import '../state/merkdito_state.dart';
import 'loja_screen.dart';

final moeda = NumberFormat.simpleCurrency(locale: 'pt_BR');

class CatalogoScreen extends StatefulWidget {
  const CatalogoScreen({super.key});
  @override State<CatalogoScreen> createState() => _CatalogoScreenState();
}

class _CatalogoScreenState extends State<CatalogoScreen> {
  String busca = '';
  int? categoria;
  @override Widget build(BuildContext context) {
    final s = context.watch<MerkditoState>();
    final categorias = s.produtos.map((p) => p.categoriaId).toSet().toList()..sort();
    final itens = s.produtos.where((p) =>
      (categoria == null || p.categoriaId == categoria) &&
      (p.nome.toLowerCase().contains(busca.toLowerCase()) ||
       (p.descricao ?? '').toLowerCase().contains(busca.toLowerCase()))).toList();
    return RefreshIndicator(
      onRefresh: s.carregarCatalogo,
      child: ListView(padding: const EdgeInsets.fromLTRB(18, 18, 18, 26), children: [
        Text('Encontre o que você precisa',
          style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.bold)),
        const SizedBox(height: 6),
        const Text('Produtos disponíveis no PDV escolhido. Retirada por conta do cliente.',
          style: TextStyle(color: Colors.black54, fontSize: 13)),
        const SizedBox(height: 18),
        TextField(
          decoration: InputDecoration(hintText: 'Buscar produtos', prefixIcon: const Icon(Icons.search),
            filled: true, fillColor: Colors.white,
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(16))),
          onChanged: (v) => setState(() => busca = v),
        ),
        const SizedBox(height: 14),
        if (categorias.length > 1)
          SingleChildScrollView(scrollDirection: Axis.horizontal, child: Row(children: [
            Padding(padding: const EdgeInsets.only(right: 8), child: ChoiceChip(
              label: const Text('Todos'), selected: categoria == null,
              onSelected: (_) => setState(() => categoria = null))),
            ...categorias.map((id) => Padding(padding: const EdgeInsets.only(right: 8), child:
              ChoiceChip(label: Text(s.produtos.firstWhere((p) => p.categoriaId == id).categoriaNome), selected: categoria == id,
                onSelected: (_) => setState(() => categoria = categoria == id ? null : id)))),
          ])),
        const SizedBox(height: 12),
        if (s.carregandoCatalogo) const LinearProgressIndicator(),
        if (!s.carregandoCatalogo && itens.isEmpty)
          const Padding(padding: EdgeInsets.symmetric(vertical: 75), child: Column(children: [
            Icon(Icons.inventory_2_outlined, size: 58, color: Colors.grey),
            SizedBox(height: 12), Text('Nenhum produto encontrado.'),
            SizedBox(height: 8), Text('Atualize o catálogo ou altere seus filtros.',
              textAlign: TextAlign.center, style: TextStyle(color: Colors.grey)),
          ])),
        ...itens.map((p) => _CartaoProduto(produto: p)),
      ]),
    );
  }
}

class _CartaoProduto extends StatelessWidget {
  final Produto produto;
  const _CartaoProduto({required this.produto});
  @override Widget build(BuildContext context) {
    final s = context.watch<MerkditoState>();
    final quantidade = s.carrinho[produto.id] ?? 0;
    return Card(margin: const EdgeInsets.only(bottom: 12), color: Colors.white,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
      child: Padding(padding: const EdgeInsets.all(14), child: Row(children: [
        _ImagemProduto(fotoUrl: produto.fotoUrl),
        const SizedBox(width: 14),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(produto.nome, maxLines: 2, overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          if (produto.descricao?.isNotEmpty == true) ...[
            const SizedBox(height: 3),
            Text(produto.descricao!, maxLines: 2, overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12, color: Colors.black54)),
          ],
          const SizedBox(height: 6),
          Text(moeda.format(produto.preco),
            style: const TextStyle(color: Color(0xFF267A42), fontWeight: FontWeight.bold, fontSize: 16)),
          Text('${produto.estoque} em estoque',
            style: const TextStyle(fontSize: 11, color: Colors.black54)),
        ])),
        Column(children: [
          IconButton(tooltip: 'Adicionar uma unidade', onPressed: quantidade >= produto.estoque ? null :
            () => s.alterarQuantidade(produto, quantidade + 1), icon: const Icon(Icons.add_circle,
              size: 29, color: Color(0xFF267A42))),
          Text('$quantidade', style: const TextStyle(fontWeight: FontWeight.bold)),
          IconButton(tooltip: 'Remover uma unidade', onPressed: quantidade <= 0 ? null :
            () => s.alterarQuantidade(produto, quantidade - 1),
            icon: const Icon(Icons.remove_circle_outline, size: 26)),
        ]),
      ])),
    );
  }
}


/// Exibe a foto cadastrada no site. Mantém o ícone padrão
/// quando o produto não possui foto ou quando o carregamento falha.
class _ImagemProduto extends StatelessWidget {
  final String? fotoUrl;

  const _ImagemProduto({this.fotoUrl});

  Widget _placeholder() {
    return Container(
      height: 83,
      width: 76,
      decoration: BoxDecoration(
        color: const Color(0xFFE8F5EB),
        borderRadius: BorderRadius.circular(15),
      ),
      child: const Icon(
        Icons.local_mall_outlined,
        size: 34,
        color: Color(0xFF267A42),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final url = fotoUrl?.trim();
    if (url == null || url.isEmpty) return _placeholder();

    return ClipRRect(
      borderRadius: BorderRadius.circular(15),
      child: SizedBox(
        height: 83,
        width: 76,
        child: Image.network(
          url,
          fit: BoxFit.contain,
          headers: const {'Accept': 'image/*'},
          loadingBuilder: (context, child, progress) {
            if (progress == null) return child;
            return _placeholder();
          },
          errorBuilder: (context, error, stackTrace) => _placeholder(),
        ),
      ),
    );
  }
}

class CarrinhoScreen extends StatefulWidget {
  const CarrinhoScreen({super.key});
  @override State<CarrinhoScreen> createState() => _CarrinhoScreenState();
}

class _CarrinhoScreenState extends State<CarrinhoScreen> {
  String? erro;
  Future<void> confirmar() async {
    final deseja = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
      title: const Text('Confirmar compra no PDV?'),
      content: const Text('Os preços e estoques serão conferidos pela API. A retirada é feita por você no PDV. '
        'Esta operação de laboratório registra a venda, mas não confirma pagamento.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Voltar')),
        FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Registrar compra')),
      ],
    ));
    if (deseja != true || !mounted) return;
    setState(() => erro = null);
    try {
      final pedido = await context.read<MerkditoState>().finalizarVenda();
      if (!mounted) return;
      await showDialog<void>(context: context, builder: (ctx) => AlertDialog(
        title: const Row(children: [Icon(Icons.check_circle_outline, color: Color(0xFF267A42)),
          SizedBox(width: 10), Expanded(child: Text('Compra registrada'))]),
        content: Text('Número: #${pedido.pedidoId}\nTotal confirmado: ${moeda.format(pedido.total)}\n'
          'Situação: ${pedido.status}\n\nRetirada pelo próprio cliente no PDV selecionado. '
          'Nenhum pagamento foi confirmado por esta versão.'),
        actions: [FilledButton(onPressed: () => Navigator.pop(ctx), child: const Text('Entendi'))],
      ));
    } catch (e) {
      if (mounted) setState(() => erro = e.toString());
    }
  }
  @override Widget build(BuildContext context) {
    final s = context.watch<MerkditoState>();
    final escolhidos = s.produtos.where((p) => s.carrinho.containsKey(p.id)).toList();
    return ListView(padding: const EdgeInsets.all(20), children: [
      Text('Minha compra', style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold)),
      const SizedBox(height: 6),
      const Text('Confira os itens que você vai retirar no mercado.', style: TextStyle(color: Colors.black54)),
      const SizedBox(height: 20),
      if (escolhidos.isEmpty) const Padding(padding: EdgeInsets.symmetric(vertical: 90), child: Column(children: [
        Icon(Icons.shopping_cart_outlined, size: 58, color: Colors.grey),
        SizedBox(height: 12), Text('Seu carrinho está vazio.'),
        SizedBox(height: 6), Text('Adicione produtos na aba Produtos.'),
      ])),
      ...escolhidos.map((p) { final qtd = s.carrinho[p.id] ?? 0;
        return Card(color: Colors.white, child: ListTile(
          title: Text(p.nome, style: const TextStyle(fontWeight: FontWeight.w600)),
          subtitle: Text('$qtd × ${moeda.format(p.preco)}'),
          trailing: Row(mainAxisSize: MainAxisSize.min, children: [
            IconButton(tooltip: 'Diminuir', onPressed: () => s.alterarQuantidade(p, qtd - 1),
              icon: const Icon(Icons.remove_circle_outline)),
            Text('$qtd', style: const TextStyle(fontWeight: FontWeight.bold)),
            IconButton(tooltip: 'Aumentar', onPressed: qtd >= p.estoque ? null :
              () => s.alterarQuantidade(p, qtd + 1), icon: const Icon(Icons.add_circle_outline)),
          ]),
        ));
      }),
      if (escolhidos.isNotEmpty) ...[
        const Divider(height: 32),
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          const Text('Estimativa', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
          Text(moeda.format(s.estimativaCarrinho),
            style: const TextStyle(fontSize: 22, fontWeight: FontWeight.bold, color: Color(0xFF267A42))),
        ]),
        const SizedBox(height: 12),
        const Text('O valor final e a disponibilidade são recalculados pela API. '
          'Não existe pagamento integrado nesta versão.',
          style: TextStyle(fontSize: 12, color: Colors.black54)),
        if (erro != null) ...[
          const SizedBox(height: 14),
          Text(erro!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
        ],
        const SizedBox(height: 20),
        FilledButton.icon(onPressed: s.finalizando ? null : confirmar,
          icon: s.finalizando ? const SizedBox(width: 18, height: 18,
            child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.task_alt),
          label: Text(s.finalizando ? 'Confirmando...' : 'Registrar compra')),
      ],
    ]);
  }
}

class HistoricoScreen extends StatelessWidget {
  const HistoricoScreen({super.key});
  @override Widget build(BuildContext context) {
    final s = context.watch<MerkditoState>();
    return RefreshIndicator(onRefresh: s.carregarHistorico,
      child: ListView(padding: const EdgeInsets.all(20), children: [
        Text('Minhas compras', style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold)),
        const SizedBox(height: 6),
        const Text('Compras registradas nesta conta, em todos os PDVs.'),
        if (s.carregandoPedidos) const Padding(padding: EdgeInsets.only(top: 14), child: LinearProgressIndicator()),
        const SizedBox(height: 16),
        if (!s.carregandoPedidos && s.pedidos.isEmpty)
          const Padding(padding: EdgeInsets.symmetric(vertical: 70),
            child: Center(child: Text('Você ainda não registrou compras.'))),
        ...s.pedidos.map((p) => Card(color: Colors.white, margin: const EdgeInsets.only(bottom: 12),
          child: ExpansionTile(
            leading: const Icon(Icons.receipt_long, color: Color(0xFF267A42)),
            title: Text('Venda #${p.pedidoId}', style: const TextStyle(fontWeight: FontWeight.bold)),
            subtitle: Text(p.criadoEm == null ? p.status :
              '${DateFormat('dd/MM/yyyy HH:mm').format(p.criadoEm!.toLocal())} · ${p.status}'),
            trailing: Text(moeda.format(p.total), style: const TextStyle(fontWeight: FontWeight.bold)),
            children: [
              ...p.itens.map((i) => ListTile(dense: true, title: Text(i.nome),
                subtitle: Text('${i.quantidade} × ${moeda.format(i.precoUnitario)}'))),
              const Padding(padding: EdgeInsets.fromLTRB(16, 0, 16, 14),
                child: Text('Compra registrada no PDV. A retirada depende da confirmação do pagamento no sistema comercial.',
                  style: TextStyle(fontSize: 12, color: Colors.black54))),
            ],
          ))),
      ]),
    );
  }
}

class PerfilScreen extends StatelessWidget {
  const PerfilScreen({super.key});
  @override Widget build(BuildContext context) {
    final s = context.watch<MerkditoState>();
    return ListView(padding: const EdgeInsets.all(22), children: [
      const CircleAvatar(radius: 32, backgroundColor: Color(0xFFE7F5EB),
        child: Icon(Icons.person, size: 36, color: Color(0xFF267A42))),
      const SizedBox(height: 14),
      Text(s.nome, textAlign: TextAlign.center,
        style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.bold)),
      Text(s.email, textAlign: TextAlign.center, style: const TextStyle(color: Colors.black54)),
      const SizedBox(height: 28),
      Card(child: Column(children: [
        ListTile(leading: const Icon(Icons.storefront_outlined), title: const Text('PDV selecionado'),
          subtitle: Text(s.lojaAtual == null ? 'Loja #${s.estabelecimentoId}' :
            '${s.lojaAtual!.empresa} · ${s.lojaAtual!.nome}')),
        const Divider(height: 1),
        const ListTile(leading: Icon(Icons.verified_user_outlined), title: Text('Segurança'),
          subtitle: Text('Sessão protegida por token e acesso autorizado pela API.')),
      ])),
      const SizedBox(height: 18),
      OutlinedButton.icon(onPressed: s.escolherOutroPdv,
        icon: const Icon(Icons.swap_horiz), label: const Text('Trocar PDV')),
      OutlinedButton.icon(onPressed: () => Navigator.push(context,
        MaterialPageRoute(builder: (_) => const GerenciarVinculosScreen())),
        icon: const Icon(Icons.add_business_outlined), label: const Text('Empresas e PDVs vinculados')),
      const SizedBox(height: 10),
      OutlinedButton.icon(onPressed: s.sair,
        icon: const Icon(Icons.logout_rounded), label: const Text('Sair da conta')),
      const SizedBox(height: 20),
      const Text('Merkdito • Mercado autônomo\n'
        'Compre pelo aplicativo e retire pessoalmente no PDV escolhido.',
        textAlign: TextAlign.center, style: TextStyle(fontSize: 12, color: Colors.grey)),
    ]);
  }
}
