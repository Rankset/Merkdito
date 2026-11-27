import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'core/theme/merkdito_theme.dart';
import 'screens/app_shell.dart';
import 'state/merkdito_state.dart';
import 'services/merkdito_api.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const MerkditoApp());
}

class MerkditoApp extends StatelessWidget {
  const MerkditoApp({super.key});
  @override Widget build(BuildContext context) => ChangeNotifierProvider(
    create: (_) => MerkditoState(MerkditoApi())..iniciar(),
    child: MaterialApp(
      title: 'Merkdito',
      debugShowCheckedModeBanner: false,
      theme: MerKditoTheme.lightTheme,
      home: const AppShell(),
    ),
  );
}
