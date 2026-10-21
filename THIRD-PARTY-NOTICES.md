# Third-party notices

SavePoint bundles the components below. Each keeps its own license.

## Fonts

The files in `src/Savepoint.App/Assets/Fonts/` are static instances cut from upstream variable fonts by `build/fonts/make-fonts.py`, because Avalonia does not render variable fonts. The script pins the upstream commits and verifies the SHA-256 of every input. The full license text for each font sits beside the font files.

### Roboto

- Files: `Roboto-Regular.ttf`, `Roboto-Medium.ttf`, `Roboto-Bold.ttf`
- Copyright 2011 The Roboto Project Authors (https://github.com/googlefonts/roboto-classic)
- License: SIL Open Font License 1.1, in `Roboto-OFL.txt`
- Source: `google/fonts` at commit `47d54ca1ebb14fd864695f1d39dc734f39017c33`, `ofl/roboto/Roboto[wdth,wght].ttf`
- Changes: instanced at width 100 and weights 400, 500 and 700; TrueType hinting instructions removed; name records rewritten so the three weights share the family "Roboto".

### Roboto Mono

- Files: `RobotoMono-Regular.ttf`, `RobotoMono-Medium.ttf`
- Copyright 2015 The Roboto Mono Project Authors (https://github.com/googlefonts/robotomono)
- License: SIL Open Font License 1.1, in `RobotoMono-OFL.txt`
- Source: `google/fonts` at commit `47d54ca1ebb14fd864695f1d39dc734f39017c33`, `ofl/robotomono/RobotoMono[wght].ttf`
- Changes: instanced at weights 400 and 500; TrueType hinting instructions removed; name records rewritten so the two weights share the family "Roboto Mono".

### Material Symbols Rounded

- Files: `MaterialSymbolsRounded.ttf`, `MaterialSymbolsRoundedFilled.ttf`
- Copyright 2026 Google LLC
- License: Apache License 2.0, in `MaterialSymbols-LICENSE.txt`
- Source: `google/material-design-icons` at commit `49d4db35df873165d6bd6ba09b063c7dafbac2f4`, `variablefont/MaterialSymbolsRounded[FILL,GRAD,opsz,wght].ttf`
- Changes: instanced at weight 400, grade 0 and optical size 24, once with FILL 0 and once with FILL 1 (renamed "Material Symbols Rounded Filled"); reduced to the glyphs listed in `build/fonts/icons.txt`; ligature and other layout tables removed, so icons are addressed by code point (`src/Savepoint.App/Theme/Icons.cs`).
