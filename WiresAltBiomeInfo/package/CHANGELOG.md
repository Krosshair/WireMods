# Changelog

## 0.1.6
- hotfix

## 0.1.5
- Fixed the translation template to include all 32 AltBiome definitions currently shipped in Valheim 1.0.16, not only the descriptors modified by the mod.
- Translation keys now use the actual AltBiome names from the game's data, such as `Kalhygge Black Forest`, `Dark Meadows`, and `BroodSwarm Mistlands`.
- Fixed translation lookup in non-English game languages by stripping the canonical English base-biome name stored inside AltBiome data before composing the localized result.

## 0.1.4
- Added external translation files and language-specific loading.
- Added a configurable `Format` line so translations can put modifiers before or after the localized base biome name.

## 0.1.3
- Removed horizontal wrapping limits from map biome names.
- Added more natural adjective-style alt-biome names.

## 0.1.2
- Reworked awkward noun-style modifier names into more natural biome descriptions.
- Fixed the dramatic first-discovery biome title for long alt-biome names.

## 0.1.1
- Fixed duplicated biome names in combined alt-biome labels.
- Added de-duplication for repeated modifier names.
- Expanded the small minimap biome text area downward.

## 0.1.0
- Initial release.
