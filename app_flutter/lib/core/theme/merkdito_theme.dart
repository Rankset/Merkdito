import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'merkdito_colors.dart';

class MerKditoTheme {
  // Border Radii
  static const double radiusSm = 8.0;
  static const double radiusMd = 14.0;
  static const double radiusLg = 20.0;
  static const double radiusPill = 999.0;

  // Sombras Suaves (Elegância e Sofisticação sem exageros)
  static final List<BoxShadow> softShadow = [
    BoxShadow(
      color: Colors.black.withOpacity(0.04),
      blurRadius: 24,
      offset: const Offset(0, 8),
      spreadRadius: 0,
    ),
  ];

  static final List<BoxShadow> cardShadow = [
    BoxShadow(
      color: const Color(0xFF2C3A47).withOpacity(0.06),
      blurRadius: 20,
      offset: const Offset(0, 6),
      spreadRadius: -2,
    ),
  ];

  static final List<BoxShadow> brandGlow = [
    BoxShadow(
      color: MerKditoColors.brandGreen.withOpacity(0.28),
      blurRadius: 20,
      offset: const Offset(0, 8),
      spreadRadius: 0,
    ),
  ];

  // Tipografia Outfit Conforme Especificação
  static TextStyle get displayLarge => GoogleFonts.outfit(
        fontSize: 32,
        fontWeight: FontWeight.w800,
        color: MerKditoColors.textDark,
        letterSpacing: -0.9,
      );

  static TextStyle get displayMedium => GoogleFonts.outfit(
        fontSize: 28,
        fontWeight: FontWeight.w800,
        color: MerKditoColors.textDark,
        letterSpacing: -0.8,
      );

  static TextStyle get headingLarge => GoogleFonts.outfit(
        fontSize: 24,
        fontWeight: FontWeight.w700,
        color: MerKditoColors.textDark,
        letterSpacing: -0.5,
      );

  static TextStyle get headingMedium => GoogleFonts.outfit(
        fontSize: 20,
        fontWeight: FontWeight.w700,
        color: MerKditoColors.textDark,
        letterSpacing: -0.4,
      );

  static TextStyle get headingSmall => GoogleFonts.outfit(
        fontSize: 16,
        fontWeight: FontWeight.w600,
        color: MerKditoColors.textDark,
      );

  static TextStyle get bodyLarge => GoogleFonts.outfit(
        fontSize: 15,
        fontWeight: FontWeight.w500,
        color: MerKditoColors.textDark,
      );

  static TextStyle get bodyMedium => GoogleFonts.outfit(
        fontSize: 14,
        fontWeight: FontWeight.w400,
        color: MerKditoColors.textDark,
        height: 1.45,
      );

  static TextStyle get subtitle => GoogleFonts.outfit(
        fontSize: 14,
        fontWeight: FontWeight.w500,
        color: MerKditoColors.textMuted,
      );

  static TextStyle get caption => GoogleFonts.outfit(
        fontSize: 12,
        fontWeight: FontWeight.w500,
        color: MerKditoColors.textMuted,
      );

  static TextStyle get priceLarge => GoogleFonts.outfit(
        fontSize: 22,
        fontWeight: FontWeight.w800,
        color: MerKditoColors.brandGreen,
        letterSpacing: -0.5,
      );

  static TextStyle get priceMedium => GoogleFonts.outfit(
        fontSize: 18,
        fontWeight: FontWeight.w700,
        color: MerKditoColors.brandGreen,
      );

  static TextStyle get badgeText => GoogleFonts.outfit(
        fontSize: 11,
        fontWeight: FontWeight.w700,
        letterSpacing: 0.3,
      );

  // ThemeData Completo
  static ThemeData get lightTheme {
    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.light,
      scaffoldBackgroundColor: MerKditoColors.background,
      primaryColor: MerKditoColors.brandGreen,
      colorScheme: const ColorScheme.light(
        primary: MerKditoColors.brandGreen,
        secondary: MerKditoColors.teal,
        surface: MerKditoColors.surface,
        background: MerKditoColors.background,
        error: MerKditoColors.red,
        onPrimary: Colors.white,
        onSecondary: Colors.white,
        onSurface: MerKditoColors.textDark,
        onBackground: MerKditoColors.textDark,
      ),
      textTheme: ThemeData.light().textTheme.apply(fontFamily: GoogleFonts.outfit().fontFamily),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        iconTheme: IconThemeData(color: MerKditoColors.textDark),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: MerKditoColors.brandGreen,
          foregroundColor: Colors.white,
          elevation: 0,
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(radiusPill),
          ),
          textStyle: GoogleFonts.outfit(
            fontSize: 16,
            fontWeight: FontWeight.w700,
          ),
        ),
      ),
      dividerTheme: const DividerThemeData(
        color: MerKditoColors.divider,
        thickness: 1,
        space: 1,
      ),
    );
  }
}

