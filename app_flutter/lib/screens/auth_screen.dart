import 'dart:async';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../state/merkdito_state.dart';
import '../widgets/brand_logo.dart';
import 'api_setup_dialog.dart';

class AcessoScreen extends StatefulWidget {
  const AcessoScreen({super.key});
  @override State<AcessoScreen> createState() => _AcessoScreenState();
}

class _AcessoScreenState extends State<AcessoScreen> with WidgetsBindingObserver {
  Timer? _reconexao;
  bool _recarregandoEmSegundoPlano = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // Quando o notebook restabelece adb reverse apos reconectar o USB,
    // o cadastro recupera a lista real do banco sem precisar reiniciar o app.
    _reconexao = Timer.periodic(const Duration(seconds: 7), (_) => _tentarReconectar());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _tentarReconectar();
  }

  Future<void> _tentarReconectar() async {
    if (!mounted || !criarConta || listando || _recarregandoEmSegundoPlano) return;
    final s = context.read<MerkditoState>();
    if (s.lojas.isNotEmpty) return;
    _recarregandoEmSegundoPlano = true;
    try {
      await s.buscarLojas();
      if (mounted && s.lojas.isNotEmpty) setState(() => mensagem = null);
    } catch (_) {
      // Sem API USB, nao falsificar empresas nem expor erros a cada tentativa.
      // O botao manual continua disponivel para diagnostico.
    } finally {
      _recarregandoEmSegundoPlano = false;
    }
  }

  final chave = GlobalKey<FormState>();
  final nome = TextEditingController();
  final email = TextEditingController();
  final senha = TextEditingController();
  bool criarConta = false;
  bool listando = false;
  final Set<int> lojasSelecionadas = {};
  String? mensagem;

  @override void dispose() {
    _reconexao?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    nome.dispose(); email.dispose(); senha.dispose();
    super.dispose();
  }

  Future<void> carregarLojas() async {
    setState(() { listando = true; mensagem = null; });
    try {
      await context.read<MerkditoState>().buscarLojas();
      if (!mounted) return;
      final lojas = context.read<MerkditoState>().lojas;
      setState(() => lojasSelecionadas.removeWhere((id) => !lojas.any((l) => l.id == id)));
    } catch (e) {
      if (mounted) setState(() => mensagem = e.toString());
    } finally {
      if (mounted) setState(() => listando = false);
    }
  }

  Future<void> enviar() async {
    if (!(chave.currentState?.validate() ?? false)) return;
    if (criarConta && lojasSelecionadas.isEmpty) {
      setState(() => mensagem = 'Selecione a empresa e a loja para vincular sua conta.');
      return;
    }
    setState(() => mensagem = null);
    try {
      final s = context.read<MerkditoState>();
      if (criarConta) {
        await s.cadastrar(nome.text, email.text, senha.text, lojasSelecionadas.toList());
      } else {
        await s.entrar(email.text, senha.text);
      }
    } catch (e) {
      if (mounted) setState(() => mensagem = e.toString());
    }
  }

  @override Widget build(BuildContext context) {
    final s = context.watch<MerkditoState>();
    return Scaffold(body: SafeArea(child: Center(child: ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 510),
      child: ListView(padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 30), children: [
        const Center(child: BrandLogo(width: 190, variant: BrandLogoVariant.lockupHorizontal)),
        const SizedBox(height: 34),
        Text(criarConta ? 'Criar minha conta' : 'Bem-vindo ao Merkdito',
          style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.bold)),
        const SizedBox(height: 7),
        Text(criarConta ? 'Cadastre-se e vincule as empresas e os PDVs onde deseja comprar.' :
          'Entre e escolha a loja onde deseja fazer sua compra.',
          style: Theme.of(context).textTheme.bodyMedium),
        const SizedBox(height: 26),
        Form(key: chave, child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          if (criarConta) ...[
            TextFormField(controller: nome, textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(labelText: 'Nome completo', prefixIcon: Icon(Icons.person_outline), border: OutlineInputBorder()),
              validator: (v) => (v?.trim().length ?? 0) < 2 ? 'Informe seu nome.' : null),
            const SizedBox(height: 14),
          ],
          TextFormField(controller: email, keyboardType: TextInputType.emailAddress,
            autofillHints: const [AutofillHints.email],
            decoration: const InputDecoration(labelText: 'E-mail', prefixIcon: Icon(Icons.mail_outline), border: OutlineInputBorder()),
            validator: (v) => (v == null || !v.contains('@') || !v.contains('.')) ? 'Informe um e-mail válido.' : null),
          const SizedBox(height: 14),
          TextFormField(controller: senha, obscureText: true,
            autofillHints: [criarConta ? AutofillHints.newPassword : AutofillHints.password],
            decoration: InputDecoration(labelText: criarConta ? 'Senha (mínimo 10 caracteres)' : 'Senha',
              prefixIcon: const Icon(Icons.lock_outline), border: const OutlineInputBorder()),
            validator: (v) => (v?.length ?? 0) < (criarConta ? 10 : 1) ? 'Confira sua senha.' : null),
          const SizedBox(height: 16),
          if (criarConta) ...[
            if (listando) const Center(child: Padding(
              padding: EdgeInsets.all(12), child: CircularProgressIndicator())),
            if (!listando && s.lojas.isEmpty)
              OutlinedButton.icon(onPressed: carregarLojas,
                icon: const Icon(Icons.storefront_outlined),
                label: const Text('Carregar empresas e lojas')),
            if (s.lojas.isNotEmpty) ...[
              const Text('Empresas e lojas (PDVs)',
                style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 7),
              const Text('Você pode escolher mais de uma empresa e PDV.',
                style: TextStyle(fontSize: 12, color: Colors.black54)),
              ...s.lojas.map((l) => CheckboxListTile(
                dense: true,
                title: Text(l.empresa, style: const TextStyle(fontWeight: FontWeight.w600)),
                subtitle: Text('PDV: ${l.nome}'),
                value: lojasSelecionadas.contains(l.id),
                onChanged: (v) => setState(() => v == true
                    ? lojasSelecionadas.add(l.id) : lojasSelecionadas.remove(l.id)),
              )),
              Text('${lojasSelecionadas.length} PDV(s) selecionado(s)',
                style: const TextStyle(fontSize: 12, color: Colors.black54)),
            ],
            const SizedBox(height: 16),
          ],
          if (mensagem != null)
            Padding(padding: const EdgeInsets.only(bottom: 14), child: Text(mensagem!,
              style: TextStyle(color: Theme.of(context).colorScheme.error))),
          FilledButton(
            onPressed: s.autenticando || listando ? null : enviar,
            child: Padding(padding: const EdgeInsets.symmetric(vertical: 5),
              child: Text(s.autenticando ? 'Aguarde...' : criarConta ? 'Criar conta e entrar' : 'Entrar')),
          ),
          const SizedBox(height: 12),
          TextButton(onPressed: s.autenticando ? null : () {
            setState(() { criarConta = !criarConta; mensagem = null; });
            if (criarConta && s.lojas.isEmpty) carregarLojas();
          }, child: Text(criarConta ? 'Já tenho conta · Entrar' : 'Ainda não tenho conta · Cadastrar')),
        ])),
        const SizedBox(height: 10),
        OutlinedButton.icon(
          icon: const Icon(Icons.settings_ethernet),
          label: const Text('Configurar conexão com o servidor'),
          onPressed: s.autenticando ? null : () async {
            final mudou = await showDialog<bool>(context: context,
              builder: (_) => const ConfigurarServidorDialog());
            if (!mounted) return;
            if (mudou == true) {
              setState(() { mensagem = null; lojasSelecionadas.clear(); });
              if (criarConta) await carregarLojas();
            }
          },
        ),
        const SizedBox(height: 30),
        const Center(child: Text('Seus dados são acessados somente pela API do Merkdito.',
          textAlign: TextAlign.center, style: TextStyle(fontSize: 12, color: Colors.grey))),
      ]),
    ))));
  }
}
