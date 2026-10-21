// Generates tests/Savepoint.App.Tests/Theme/Material/golden-mcu-0.3.0.json: reference output of the
// official @material/material-color-utilities 0.3.0, the version SavePoint v1 pinned. The C# port in
// src/Savepoint.App/Theme/Material is checked against this file.
//
// Run from the repo root:
//   cd .tools/mcu && npm init -y && npm install @material/material-color-utilities@0.3.0 && cd ../..
//   node build/theme/generate-golden.mjs
//
// Generated with node v24.18.0 and npm 11.12.1 (Windows 11), package 0.3.0 from the npm registry.
//
// Schemes are built the way v1's src/renderer/theme/palette.ts builds them:
//   new SchemeTonalSpot(Hct.fromInt(argbFromHex(seed)), isDark, 0) and role.getArgb(scheme).
// Extra schemes (other contrast levels, monochrome, fidelity-style palettes) exercise the ported
// branches that the contrast-0 tonal-spot palette never reaches.

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageRoot = new URL('../../.tools/mcu/node_modules/@material/material-color-utilities/', import.meta.url);
const mcu = await import(new URL('index.js', packageRoot).href);
const { Variant } = await import(new URL('dynamiccolor/variant.js', packageRoot).href);
const {
  argbFromHex, Hct, MaterialDynamicColors, DynamicColor, DynamicScheme, SchemeTonalSpot, SchemeMonochrome,
  TonalPalette, ViewingConditions, DislikeAnalyzer, Contrast,
} = mcu;

const outPath = fileURLToPath(
  new URL('../../tests/Savepoint.App.Tests/Theme/Material/golden-mcu-0.3.0.json', import.meta.url));

const SEEDS = [
  ['blue', '#2986cc'],
  ['violet', '#7c4dff'],
  ['green', '#2e7d32'],
  ['amber', '#f9a825'],
  ['crimson', '#c2185b'],
];

const hex = (argb) => '#' + (argb >>> 0).toString(16).padStart(8, '0').toUpperCase();

const roleNames = Object.getOwnPropertyNames(MaterialDynamicColors)
  .filter((name) => MaterialDynamicColors[name] instanceof DynamicColor);
if (roleNames.length !== 54) {
  throw new Error(`expected 54 dynamic color roles, found ${roleNames.length}`);
}

const PALETTES = ['primaryPalette', 'secondaryPalette', 'tertiaryPalette', 'neutralPalette', 'neutralVariantPalette'];

function describeScheme(id, group, variant, seedName, scheme) {
  const colors = {};
  const tones = {};
  for (const name of roleNames) {
    colors[name] = hex(MaterialDynamicColors[name].getArgb(scheme));
    tones[name] = MaterialDynamicColors[name].getTone(scheme);
  }
  const palettes = {};
  for (const key of PALETTES) {
    const palette = scheme[key];
    palettes[key] = { hue: palette.hue, chroma: palette.chroma, keyColor: hex(palette.keyColor.toInt()) };
  }
  return {
    id, group, variant, seed: seedName, sourceColor: hex(scheme.sourceColorArgb), dark: scheme.isDark,
    contrast: scheme.contrastLevel, palettes, colors, tones,
  };
}

function customScheme(variant, hct, isDark, contrast, palettes) {
  return new DynamicScheme({
    sourceColorArgb: hct.toInt(), variant, contrastLevel: contrast, isDark,
    primaryPalette: TonalPalette.fromHueAndChroma(...palettes.primary),
    secondaryPalette: TonalPalette.fromHueAndChroma(...palettes.secondary),
    tertiaryPalette: TonalPalette.fromHueAndChroma(...palettes.tertiary),
    neutralPalette: TonalPalette.fromHueAndChroma(...palettes.neutral),
    neutralVariantPalette: TonalPalette.fromHueAndChroma(...palettes.neutralVariant),
  });
}

const schemes = [];
const label = (seed, dark, contrast) => `${seed}-${dark ? 'dark' : 'light'}-c${contrast}`;

// Group "v1": exactly what palette.ts derives, for every preset and both modes.
for (const [name, seedHex] of SEEDS) {
  for (const dark of [true, false]) {
    const hct = Hct.fromInt(argbFromHex(seedHex));
    schemes.push(describeScheme(`tonalSpot-${label(name, dark, 0)}`, 'v1', 'tonalSpot', name,
      new SchemeTonalSpot(hct, dark, 0)));
  }
}

// Group "contrast": the contrast curves and the reduced/raised contrast tone paths.
for (const [name, seedHex] of SEEDS.filter(([n]) => ['blue', 'amber', 'crimson'].includes(n))) {
  for (const dark of [true, false]) {
    for (const contrast of [-1, -0.5, 0.25, 0.5, 0.75, 1]) {
      const hct = Hct.fromInt(argbFromHex(seedHex));
      schemes.push(describeScheme(`tonalSpot-${label(name, dark, contrast)}`, 'contrast', 'tonalSpot', name,
        new SchemeTonalSpot(hct, dark, contrast)));
    }
  }
}

// Group "monochrome": the variant branches in the role table, using the library's own scheme.
for (const [name, seedHex] of SEEDS.filter(([n]) => ['blue', 'amber'].includes(n))) {
  for (const dark of [true, false]) {
    for (const contrast of [-1, 0, 1]) {
      const hct = Hct.fromInt(argbFromHex(seedHex));
      schemes.push(describeScheme(`monochrome-${label(name, dark, contrast)}`, 'monochrome', 'monochrome', name,
        new SchemeMonochrome(hct, dark, contrast)));
    }
  }
}

// Group "fidelity": SchemeFidelity needs the temperature cache, which is not ported, so the palettes
// are assembled the same way with a chosen tertiary. The blue case uses a disliked-zone tertiary
// (hue 100, chroma 40) so the dislike fix-up path runs.
const fidelityTertiary = { blue: [100, 40], amber: [330, 30], crimson: [160, 24] };
for (const [name, seedHex] of SEEDS.filter(([n]) => n in fidelityTertiary)) {
  for (const dark of [true, false]) {
    for (const contrast of [-1, 0, 0.5, 1]) {
      const hct = Hct.fromInt(argbFromHex(seedHex));
      const palettes = {
        primary: [hct.hue, hct.chroma],
        secondary: [hct.hue, Math.max(hct.chroma - 32.0, hct.chroma * 0.5)],
        tertiary: fidelityTertiary[name],
        neutral: [hct.hue, hct.chroma / 8.0],
        neutralVariant: [hct.hue, hct.chroma / 8.0 + 4.0],
      };
      schemes.push(describeScheme(`fidelity-${label(name, dark, contrast)}`, 'fidelity', 'fidelity', name,
        customScheme(Variant.FIDELITY, hct, dark, contrast, palettes)));
    }
  }
}

// HCT: argb -> hue/chroma/tone.
const hctColors = [
  '#000000', '#ffffff', '#777777', '#808080', '#ff0000', '#00ff00', '#0000ff', '#ffff00', '#00ffff', '#ff00ff',
  '#123456', '#abcdef', '#fedcba', '#010101', '#fefefe', '#7f7f7f', '#ff8000', '#80ff00', '#0080ff', '#8000ff',
  ...SEEDS.map(([, seedHex]) => seedHex),
];
const hctFromArgb = hctColors.map((colorHex) => {
  const argb = argbFromHex(colorHex);
  const hct = Hct.fromInt(argb);
  return { argb: hex(argb), hue: hct.hue, chroma: hct.chroma, tone: hct.tone };
});

// HCT: (hue, chroma, tone) -> argb, then the argb read back. Chroma 200 forces the gamut-limit bisection.
const hctSolved = [];
for (const hue of [0, 25, 90, 150, 250, 285, 359.9]) {
  for (const chroma of [0, 12, 36, 84, 200]) {
    for (const tone of [0, 0.00005, 12.5, 50, 65, 87, 99.99995, 100]) {
      const hct = Hct.from(hue, chroma, tone);
      hctSolved.push({
        hue, chroma, tone, argb: hex(hct.toInt()), outHue: hct.hue, outChroma: hct.chroma, outTone: hct.tone,
      });
    }
  }
}

// Tonal palettes, including a fractional tone and a hue past 360 that the constructor sanitizes.
const paletteTones = [0, 4, 5, 6, 10, 12, 12.5, 17, 20, 22, 24, 25, 30, 35, 37.7, 40, 50, 60, 70, 80, 87, 90, 92, 94, 95, 96, 98, 99, 100];
const paletteInputs = [[250, 36], [25, 84], [285, 16], [100, 24], [0, 0], [359, 6], [135.5, 48], [60, 200]];
const tonalPalettes = paletteInputs.map(([hue, chroma]) => {
  const palette = TonalPalette.fromHueAndChroma(hue, chroma);
  const tones = {};
  for (const tone of paletteTones) {
    tones[tone] = hex(palette.tone(tone));
  }
  const key = palette.keyColor;
  return { hue, chroma, keyColor: hex(key.toInt()), keyHue: key.hue, keyChroma: key.chroma, keyTone: key.tone, tones };
});

const tonalFromInt = SEEDS.map(([, seedHex]) => {
  const palette = TonalPalette.fromInt(argbFromHex(seedHex));
  const tones = {};
  for (const tone of [10, 50, 90]) {
    tones[tone] = hex(palette.tone(tone));
  }
  return { seed: seedHex, hue: palette.hue, chroma: palette.chroma, keyColor: hex(palette.keyColor.toInt()), tones };
});

// Dislike analyzer around the dark yellow-green zone and its edges.
const dislike = [];
for (const hue of [89, 90, 100, 111, 112]) {
  for (const chroma of [10, 16, 17, 30]) {
    for (const tone of [40, 64, 65, 80]) {
      const hct = Hct.from(hue, chroma, tone);
      dislike.push({
        argb: hex(hct.toInt()), disliked: DislikeAnalyzer.isDisliked(hct),
        fixed: hex(DislikeAnalyzer.fixIfDisliked(hct).toInt()),
      });
    }
  }
}

// Contrast helpers, including the -1 "not reachable" results.
const contrastTones = [0, 10, 30, 49.5, 50, 60, 85, 100];
const contrastRatios = [1.5, 3, 4.5, 7, 11, 21];
const contrast = {
  ratios: contrastTones.flatMap((a) => contrastTones.map((b) => ({ a, b, ratio: Contrast.ratioOfTones(a, b) }))),
  lighter: contrastTones.flatMap((tone) => contrastRatios.map((ratio) => ({ tone, ratio, result: Contrast.lighter(tone, ratio) }))),
  darker: contrastTones.flatMap((tone) => contrastRatios.map((ratio) => ({ tone, ratio, result: Contrast.darker(tone, ratio) }))),
};

const vc = ViewingConditions.DEFAULT;
const viewingConditions = {
  n: vc.n, aw: vc.aw, nbb: vc.nbb, ncb: vc.ncb, c: vc.c, nc: vc.nc, rgbD: vc.rgbD, fl: vc.fl, fLRoot: vc.fLRoot, z: vc.z,
};

const golden = {
  meta: {
    package: '@material/material-color-utilities',
    version: '0.3.0',
    node: process.version,
    note: 'ARGB values are #AARRGGBB. Group "v1" is what SavePoint v1 derives; the other groups exercise ported branches.',
  },
  roles: roleNames,
  seeds: Object.fromEntries(SEEDS),
  viewingConditions,
  schemes,
  hctFromArgb,
  hctSolved,
  tonalPalettes,
  tonalFromInt,
  dislike,
  contrast,
};

// Section records stay one per line; each scheme is expanded so its colors and tones sit on one line each.
const isPrimitive = (v) => v === null || typeof v !== 'object';
function format(value, depth = 0) {
  if (isPrimitive(value) || depth >= 3) return JSON.stringify(value);
  const pad = '  '.repeat(depth + 1);
  const end = '  '.repeat(depth);
  if (Array.isArray(value)) {
    if (value.every(isPrimitive)) return JSON.stringify(value);
    const items = value.map((item) => pad + ('colors' in item ? format(item, depth + 1) : JSON.stringify(item)));
    return '[\n' + items.join(',\n') + '\n' + end + ']';
  }
  const items = Object.entries(value).map(([key, item]) => pad + JSON.stringify(key) + ': ' + format(item, depth + 1));
  return '{\n' + items.join(',\n') + '\n' + end + '}';
}

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, format(golden) + '\n');
console.log(`wrote ${outPath}: ${schemes.length} schemes, ${roleNames.length} roles`);
