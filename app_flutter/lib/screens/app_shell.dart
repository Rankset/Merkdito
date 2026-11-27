import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../state/merkdito_state.dart';
import '../widgets/brand_logo.dart';
import 'auth_screen.dart';
import 'commerce_screens.dart';
import 'loja_screen.dart';

class AppShell extends StatelessWidget {
  const AppShell({super.key});
  @override Widget build(BuildContext context) {
    final state = context.watch<MerkditoState>();
    if (!state.inicializado) {
      return const Scaffold(body: Center(child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [BrandLogo(width: 170), SizedBox(height: 26), CircularProgressIndicator()],
      )));
    }
    if (!state.autenticado) return const AcessoScreen();
    if (!state.lojaEscolhida) return const EscolherLojaScreen();
    return ComprasShell(key: ValueKey(state.estabelecimentoId));
  }
}

class ComprasShell extends StatefulWidget {
  const ComprasShell({super.key});
  @override State<ComprasShell> createState() => _ComprasShellState();
}

class _ComprasShellState extends State<ComprasShell> with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState lifecycle) {
    if (lifecycle == AppLifecycleState.resumed && mounted) {
      final s = context.read<MerkditoState>();
      if (s.autenticado && !s.finalizando) s.atualizarDados();
    }
  }

  int indice = 0;
  @override Widget build(BuildContext context) {
    final state = context.watch<MerkditoState>();
    final loja = state.lojaAtual;
    return Scaffold(
      appBar: AppBar(
        title: const BrandLogo(width: 130),
        actions: [
          IconButton(
            tooltip: 'Atualizar produtos e pedidos',
            onPressed: state.carregandoCatalogo ? null : state.atualizarDados,
            icon: const Icon(Icons.refresh_rounded),
          ),
        ],
      ),
      body: Column(children: [
        Container(
          width: double.infinity,
          padding: const EdgeInsets.fromLTRB(18, 10, 18, 10),
          color: const Color(0xFFE7F5EB),
          child: InkWell(
            onTap: state.finalizando ? null : state.escolherOutroPdv,
            child: Row(children: [
              const Icon(Icons.storefront_outlined, size: 19, color: Color(0xFF267A42)),
              const SizedBox(width: 9),
              Expanded(child: Text(loja == null ? 'PDV #${state.estabelecimentoId}' :
                '${loja.empresa} · ${loja.nome}', maxLines: 2,
                style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13))),
              const Icon(Icons.swap_horiz, color: Color(0xFF267A42)),
              const SizedBox(width: 5),
              const Text('Trocar', style: TextStyle(fontWeight: FontWeight.w600)),
            ]),
          ),
        ),
        if (state.aviso != null)
          MaterialBanner(
            content: Text(state.aviso!),
            actions: [TextButton(onPressed: state.atualizarDados, child: const Text('Tentar novamente'))],
          ),
        Expanded(child: IndexedStack(index: indice, children: const [
          CatalogoScreen(), CarrinhoScreen(), HistoricoScreen(), PerfilScreen(),
        ])),
      ]),
      bottomNavigationBar: NavigationBar(
        selectedIndex: indice,
        onDestinationSelected: (v) {
          setState(() => indice = v);
          if (v == 2) context.read<MerkditoState>().carregarHistorico();
        },
        destinations: [
          const NavigationDestination(icon: Icon(Icons.grid_view_rounded), label: 'Produtos'),
          NavigationDestination(icon: Badge(
              isLabelVisible: state.quantidadeCarrinho > 0,
              label: Text('${state.quantidadeCarrinho}'),
              child: const Icon(Icons.shopping_bag_outlined)), label: 'Carrinho'),
          const NavigationDestination(icon: Icon(Icons.receipt_long_outlined), label: 'Pedidos'),
          const NavigationDestination(icon: Icon(Icons.person_outline), label: 'Conta'),
        ],
      ),
    );
  }
}
