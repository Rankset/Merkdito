import 'package:flutter/material.dart';

/// Paleta de Cores Oficial do MerKdito (Fiel ao portal web e Brand System)
class MerKditoColors {
  // Cores Primárias da Marca
  static const Color brandGreen = Color(0xFF5DA54A);
  static const Color brandGreenLight = Color(0xFF6BB656);
  static const Color brandGreenDark = Color(0xFF4E9440);

  // Paleta Pastel (Accent Colors do Sistema)
  static const Color teal = Color(0xFF2AB0A3);
  static const Color tealLight = Color(0xFFE6F7F5);
  static const Color yellow = Color(0xFFFFE458);
  static const Color yellowLight = Color(0xFFFFFBE8);
  static const Color orange = Color(0xFFFE9A34);
  static const Color orangeLight = Color(0xFFFFF3E7);
  static const Color red = Color(0xFFFF544C);
  static const Color redLight = Color(0xFFFFEEED);

  // Fundos e Superfícies
  static const Color background = Color(0xFFFFFAF0); // Cream suave do site
  static const Color surface = Colors.white;
  static const Color creamDark = Color(0xFFFFF0C2);
  static const Color cardBg = Color(0xFFFFFFFF);

  // Tipografia e Contrastes
  static const Color textDark = Color(0xFF2C3A47);
  static const Color textMuted = Color(0xFF6B7A87);
  static const Color textLight = Color(0xFF94A3B8);

  // Vidro / Glassmorphism
  static Color glassBackground = Colors.white.withOpacity(0.72);
  static Color glassBorder = Colors.white.withOpacity(0.45);
  static const Color divider = Color(0x14000000);
}
