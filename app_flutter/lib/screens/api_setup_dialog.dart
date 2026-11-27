import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../state/merkdito_state.dart';

/// Configuração de laboratório. Não altera banco nem instala servidor.
class ConfigurarServidorDialog extends StatefulWidget {
  const ConfigurarServidorDialog({super.key});
  @override State<ConfigurarServidorDialog> createState() => _ConfigurarServidorDialogState();
}

class _ConfigurarServidorDialogState extends State<ConfigurarServidorDialog> {
  final _endereco = TextEditingController();
  bool _iniciado = false;
  bool _testando = false;
  String? _erro;
  @override void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_iniciado) {
      _endereco.text = context.read<MerkditoState>().api.baseUrl;
      _iniciado = true;
    }
  }
  @override void dispose() { _endereco.dispose(); super.dispose(); }
  Future<void> _salvar() async {
    if (_testando) return;
    setState(() { _testando = true; _erro = null; });
    try {
      await context.read<MerkditoState>().configurarServidor(_endereco.text);
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _erro = e.toString());
    } finally { if (mounted) setState(() => _testando = false); }
  }
  @override Widget build(BuildContext context) => AlertDialog(
    title: const Text('Conectar ao servidor Merkdito'),
    content: SingleChildScrollView(child: Column(
      mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text('Para testar com a API local no notebook, conecte o celular via USB e execute adb reverse tcp:5102 tcp:5102.'),
        const SizedBox(height: 12),
        TextField(controller: _endereco, enabled: !_testando,
          autocorrect: false, keyboardType: TextInputType.url,
          decoration: const InputDecoration(
            border: OutlineInputBorder(), labelText: 'Endereço da API',
            hintText: 'http://127.0.0.1:5102', prefixIcon: Icon(Icons.dns_outlined))),
        const SizedBox(height: 12),
        const Text('127.0.0.1 chega ao notebook apenas enquanto adb reverse estiver ativo. Sem USB, configure um servidor HTTPS acessível.',
          style: TextStyle(fontSize: 12)),
        if (_erro != null) ...[
          const SizedBox(height: 12),
          Text(_erro!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
        ],
      ],
    )),
    actions: [
      TextButton(onPressed: _testando ? null : () => Navigator.pop(context),
        child: const Text('Cancelar')),
      FilledButton.icon(onPressed: _testando ? null : _salvar,
        icon: _testando ? const SizedBox(height: 16,width: 16,child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.wifi_find),
        label: Text(_testando ? 'Testando...' : 'Testar e salvar')),
    ],
  );
}
