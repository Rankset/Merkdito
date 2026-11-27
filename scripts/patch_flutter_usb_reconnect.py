#!/usr/bin/env python3
"""Adiciona reconexao automatica de PDVs no Android, sem mocks nem mudanca no backend.

Pode ser aplicado ao pacote v1.5 original uma unica vez. Falha se o codigo mudar,
impedindo patches silenciosos sobre fontes incompatíveis.
"""
from pathlib import Path

base = Path(__file__).resolve().parents[1]

def trocar(rel: str, anterior: str, novo: str):
    p = base / rel
    txt = p.read_text(encoding='utf-8')
    if novo in txt:
        print(f'Ja aplicado: {rel}')
        return
    assert txt.count(anterior) == 1, f'Trecho nao encontrado/duplicado em {rel}: {anterior[:100]!r}'
    p.write_text(txt.replace(anterior, novo, 1), encoding='utf-8')
    print(f'Aplicado: {rel}')

rel='app_flutter/lib/screens/auth_screen.dart'
trocar(rel, "import 'package:flutter/material.dart';", "import 'dart:async';\nimport 'package:flutter/material.dart';")
trocar(rel, 'class _AcessoScreenState extends State<AcessoScreen> {', '''class _AcessoScreenState extends State<AcessoScreen> with WidgetsBindingObserver {
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
''')
trocar(rel, '  @override void dispose() { nome.dispose(); email.dispose(); senha.dispose(); super.dispose(); }',
'''  @override void dispose() {
    _reconexao?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    nome.dispose(); email.dispose(); senha.dispose();
    super.dispose();
  }''')

rel='app_flutter/lib/screens/app_shell.dart'
trocar(rel,'class _ComprasShellState extends State<ComprasShell> {','''class _ComprasShellState extends State<ComprasShell> with WidgetsBindingObserver {
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
''')

rel='app_flutter/lib/services/merkdito_api.dart'
trocar(rel,"static const enderecoPadrao = String.fromEnvironment('MERKDITO_API_URL', defaultValue: '');",
           "static const enderecoPadrao = String.fromEnvironment('MERKDITO_API_URL', defaultValue: 'http://127.0.0.1:5102');")
print('USB: recuperacao automatica de lojas, refresh ao retomar e URL padrao local ativadas.')
