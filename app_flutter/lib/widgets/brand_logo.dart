import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

enum BrandLogoVariant {
  symbol,
  symbolWhite,
  wordmark,
  wordmarkWhite,
  wordmarkDark,
  lockupHorizontal,
  lockupHorizontalDark,
  lockupVertical,
}

class BrandLogo extends StatelessWidget {
  final BrandLogoVariant variant;
  final double? width;
  final double? height;
  final BoxFit fit;

  const BrandLogo({
    super.key,
    this.variant = BrandLogoVariant.lockupHorizontal,
    this.width,
    this.height,
    this.fit = BoxFit.contain,
  });

  String get _svgAssetPath {
    switch (variant) {
      case BrandLogoVariant.symbol:
        return 'assets/brand/svg/merkdito-symbol.svg';
      case BrandLogoVariant.symbolWhite:
        return 'assets/brand/svg/merkdito-symbol-white.svg';
      case BrandLogoVariant.wordmark:
        return 'assets/brand/svg/merkdito-wordmark.svg';
      case BrandLogoVariant.wordmarkWhite:
        return 'assets/brand/svg/merkdito-wordmark-white.svg';
      case BrandLogoVariant.wordmarkDark:
        return 'assets/brand/svg/merkdito-wordmark-dark.svg';
      case BrandLogoVariant.lockupHorizontal:
        return 'assets/brand/svg/merkdito-lockup-horizontal.svg';
      case BrandLogoVariant.lockupHorizontalDark:
        return 'assets/brand/svg/merkdito-lockup-horizontal-dark.svg';
      case BrandLogoVariant.lockupVertical:
        return 'assets/brand/svg/merkdito-lockup-vertical.svg';
    }
  }

  String get _pngFallbackPath {
    switch (variant) {
      case BrandLogoVariant.symbol:
        return 'assets/brand/png/symbol_green_1024.png';
      case BrandLogoVariant.symbolWhite:
        return 'assets/brand/png/symbol_white_1024.png';
      case BrandLogoVariant.wordmark:
        return 'assets/brand/png/wordmark_green.png';
      case BrandLogoVariant.wordmarkWhite:
        return 'assets/brand/png/wordmark_white.png';
      case BrandLogoVariant.wordmarkDark:
        return 'assets/brand/png/wordmark_dark.png';
      case BrandLogoVariant.lockupHorizontal:
        return 'assets/brand/png/lockup_horizontal.png';
      case BrandLogoVariant.lockupHorizontalDark:
        return 'assets/brand/png/lockup_horizontal_dark.png';
      case BrandLogoVariant.lockupVertical:
        return 'assets/brand/png/lockup_vertical.png';
    }
  }

  @override
  Widget build(BuildContext context) {
    return SvgPicture.asset(
      _svgAssetPath,
      width: width,
      height: height,
      fit: fit,
      placeholderBuilder: (BuildContext context) => Image.asset(
        _pngFallbackPath,
        width: width,
        height: height,
        fit: fit,
      ),
    );
  }
}
