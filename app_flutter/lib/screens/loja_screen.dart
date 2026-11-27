import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../models/consumer_models.dart';
import '../state/merkdito_state.dart';
import '../widgets/brand_logo.dart';

/// Escolha explícita da unidade para retirada presencial pelo consumidor.
class EscolherLojaScreen extends StatefulWidget {
  const EscolherLojaScreen({super.key});
  @override State<EscolherLojaScreen> createState() => _EscolherLojaScreenState();
}

class _EscolherLojaScreenState extends State<EscolherLojaScreen> {
  bool aguardando = false;
  String? erro;

  @override void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final s = context.read<MerkditoState>();
      if (s.lojas.isEmpty) {
        try { await s.buscarLojas(); }
        catch (e) { if (mounted) setState(() => erro = e.toString()); }
      }
    });
  }

  Future<void> escolher(Loja loja) async {
    if (aguardando) return;
    setState(() { aguardando = true; erro = null; });
    try { await context.read<MerkditoState>().selecionarLoja(loja.id); }
    catch (e) { if (mounted) setState(() => erro = e.toString()); }
    finally { if (mounted) setState(() => aguardando = false); }
  }

  @override Widget build(BuildContext context) {
    final state = context.watch<MerkditoState>();
    final lojas = state.lojasVinculadas;
    return Scaffold(
      appBar: AppBar(title: const BrandLogo(width: 135), actions: [
        IconButton(tooltip: 'Sair', onPressed: state.sair,
          icon: const Icon(Icons.logout_outlined)),
      ]),
      body: ListView(padding: const EdgeInsets.all(22), children: [
        Text('Escolha o mercado', style: Theme.of(context).textTheme.headlineSmall?.copyWith(
          fontWeight: FontWeight.bold)),
        const SizedBox(height: 8),
        const Text('Selecione o PDV onde deseja comprar. Depois você mesmo retira os produtos na loja.',
          style: TextStyle(color: Colors.black54)),
        const SizedBox(height: 20),
        if (aguardando) const LinearProgressIndicator(),
        if (erro != null) Padding(padding: const EdgeInsets.only(bottom: 12),
          child: Text(erro!, style: const TextStyle(color: Colors.red))),
        if (lojas.isEmpty) ...[
          const Padding(padding: EdgeInsets.all(18), child: Text(
            'Nenhuma loja vinculada está disponível. Confira sua conexão ou atualize seus vínculos.')),
          OutlinedButton(onPressed: () async {
            try { await state.buscarLojas(); }
            catch (e) { if (mounted) setState(() => erro = e.toString()); }
          }, child: const Text('Atualizar lista de empresas e PDVs')),
        ],
        ...lojas.map((loja) => Card(color: Colors.white, margin: const EdgeInsets.only(bottom: 12),
          child: ListTile(
            leading: const CircleAvatar(backgroundColor: Color(0xFFE7F5EB),
              child: Icon(Icons.storefront, color: Color(0xFF267A42))),
            title: Text(loja.empresa, style: const TextStyle(fontWeight: FontWeight.bold)),
            subtitle: Text('PDV: ${loja.nome} · Retirada pelo cliente'),
            trailing: const Icon(Icons.chevron_right_rounded),
            onTap: aguardando ? null : () => escolher(loja),
          ))),
        const SizedBox(height: 16),
        OutlinedButton.icon(onPressed: () => Navigator.push(context,
          MaterialPageRoute(builder: (_) => const GerenciarVinculosScreen())),
          icon: const Icon(Icons.add_business), label: const Text('Gerenciar empresas e PDVs')),
      ]),
    );
  }
}

class GerenciarVinculosScreen extends StatefulWidget {
  const GerenciarVinculosScreen({super.key});
  @override State<GerenciarVinculosScreen> createState() => _GerenciarVinculosScreenState();
}

class _GerenciarVinculosScreenState extends State<GerenciarVinculosScreen> {
  final Set<int> selecionadas = {};
  bool carregado = false;
  bool salvando = false;
  String? erro;

  @override void didChangeDependencies() {
    super.didChangeDependencies();
    if (!carregado) {
      selecionadas.addAll(context.read<MerkditoState>().estabelecimentoIds);
      carregado = true;
    }
  }

  @override Widget build(BuildContext context) {
    final state = context.watch<MerkditoState>();
    return Scaffold(
      appBar: AppBar(title: const Text('Meus PDVs')),
      body: ListView(padding: const EdgeInsets.all(18), children: [
        const Text('Escolha as empresas e os PDVs que deseja vincular ao seu cadastro.',
          style: TextStyle(color: Colors.black54)),
        const SizedBox(height: 14),
        if (state.lojas.isEmpty) OutlinedButton(
          onPressed: () async {
            try { await state.buscarLojas(); }
            catch (e) { if (mounted) setState(() => erro = e.toString()); }
          }, child: const Text('Carregar lista')),
        ...state.lojas.map((loja) => Card(color: Colors.white, child: CheckboxListTile(
          title: Text(loja.empresa, style: const TextStyle(fontWeight: FontWeight.w600)),
          subtitle: Text('PDV: ${loja.nome}'),
          value: selecionadas.contains(loja.id),
          onChanged: salvando ? null : (v) => setState(() => v == true
              ? selecionadas.add(loja.id) : selecionadas.remove(loja.id)),
        ))),
        if (erro != null) Text(erro!, style: const TextStyle(color: Colors.red)),
        const SizedBox(height: 14),
        FilledButton.icon(onPressed: salvando ? null : () async {
          if (selecionadas.isEmpty) {
            setState(() => erro = 'Selecione ao menos um PDV.'); return;
          }
          setState(() { salvando = true; erro = null; });
          try {
            await context.read<MerkditoState>().atualizarVinculos(selecionadas.toList());
            if (context.mounted) Navigator.of(context).pop();
          } catch (e) { if (mounted) setState(() => erro = e.toString()); }
          finally { if (mounted) setState(() => salvando = false); }
        }, icon: const Icon(Icons.save_outlined),
          label: Text(salvando ? 'Salvando...' : 'Salvar meus PDVs')),
      ]),
    );
  }
}
