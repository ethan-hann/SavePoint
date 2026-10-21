#!/usr/bin/env python3
"""Builds the static fonts and the icon code-point map (decision D12).

Avalonia does not render variable fonts and upstream ships Roboto, Roboto Mono and
Material Symbols Rounded only as variable fonts. This script downloads pinned upstream
files, checks their SHA-256, cuts static instances with fontTools, subsets the icon
fonts to the names in icons.txt, and writes everything the app embeds.

Run it with the interpreter of a virtual environment that has build/fonts/requirements.txt
installed; nothing is installed globally:

    python -m venv .tools/venv
    .tools/venv/Scripts/python -m pip install -r build/fonts/requirements.txt   (bin/ on Linux and macOS)
    .tools/venv/Scripts/python build/fonts/make-fonts.py

Outputs (committed):
    src/Savepoint.App/Assets/Fonts/*.ttf and the license files beside them
    src/Savepoint.App/Theme/Icons.cs
"""

from __future__ import annotations

import hashlib
import re
import shutil
import sys
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path

import fontTools
from fontTools import subset
from fontTools.pens.areaPen import AreaPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables import ttProgram
from fontTools.varLib import instancer

try:
    import uharfbuzz as hb
except ImportError:
    raise SystemExit("error: uharfbuzz is missing; install build/fonts/requirements.txt into the virtual environment")

REQUIRED_FONTTOOLS = "4.67.0"

ROOT = Path(__file__).resolve().parents[2]
SOURCE_DIR = ROOT / ".tools" / "fonts-src"
STAGE_DIR = ROOT / ".tools" / "fonts-build"
OUTPUT_DIR = ROOT / "src" / "Savepoint.App" / "Assets" / "Fonts"
ICONS_TXT = Path(__file__).resolve().parent / "icons.txt"
ICONS_CS = ROOT / "src" / "Savepoint.App" / "Theme" / "Icons.cs"

# Pinned commits; never `main`, so a rebuild reproduces the committed fonts.
FONTS_COMMIT = "47d54ca1ebb14fd864695f1d39dc734f39017c33"  # google/fonts
ICONS_COMMIT = "49d4db35df873165d6bd6ba09b063c7dafbac2f4"  # google/material-design-icons


@dataclass(frozen=True)
class Source:
    local_name: str
    repo: str
    commit: str
    path: str
    sha256: str

    @property
    def url(self) -> str:
        quoted = urllib.parse.quote(self.path, safe="/")
        return f"https://raw.githubusercontent.com/{self.repo}/{self.commit}/{quoted}"


ROBOTO = Source(
    "Roboto[wdth,wght].ttf", "google/fonts", FONTS_COMMIT, "ofl/roboto/Roboto[wdth,wght].ttf",
    "d7598e12c5dbef095ff8272cfc55da0250bd07fbdecbac8a530b9b277872a134")
ROBOTO_OFL = Source(
    "Roboto-OFL.txt", "google/fonts", FONTS_COMMIT, "ofl/roboto/OFL.txt",
    "061402327a96aadb0bfb694a960ed289ecd38d383e396243831ab81feb109c41")
ROBOTO_MONO = Source(
    "RobotoMono[wght].ttf", "google/fonts", FONTS_COMMIT, "ofl/robotomono/RobotoMono[wght].ttf",
    "66a80e79d17e4c7cabd162e2916578a4cc08fd19eef6e2a643305eae9c567b2b")
ROBOTO_MONO_OFL = Source(
    "RobotoMono-OFL.txt", "google/fonts", FONTS_COMMIT, "ofl/robotomono/OFL.txt",
    "50ab8dd54680d3473f649c9db86fece88434d097c7834475c1c72d2f8c429215")
SYMBOLS = Source(
    "MaterialSymbolsRounded[FILL,GRAD,opsz,wght].ttf", "google/material-design-icons", ICONS_COMMIT,
    "variablefont/MaterialSymbolsRounded[FILL,GRAD,opsz,wght].ttf",
    "32e4011709e055596eb8d2dd0f7cb547b47c1d40ac9ecb8fad54f7f7eac202e7")
SYMBOLS_CODEPOINTS = Source(
    "MaterialSymbolsRounded[FILL,GRAD,opsz,wght].codepoints", "google/material-design-icons", ICONS_COMMIT,
    "variablefont/MaterialSymbolsRounded[FILL,GRAD,opsz,wght].codepoints",
    "0c9dae3ed678a81549c3be046bc8045335e5f57f2cbe2287360fbf0c2cc1355f")
SYMBOLS_LICENSE = Source(
    "MaterialSymbols-LICENSE.txt", "google/material-design-icons", ICONS_COMMIT, "LICENSE",
    "58d1e17ffe5109a7ae296caafcadfdbe6a7d176f0bc4ab01e12a689b0499d8bd")

ALL_SOURCES = [ROBOTO, ROBOTO_OFL, ROBOTO_MONO, ROBOTO_MONO_OFL, SYMBOLS, SYMBOLS_CODEPOINTS, SYMBOLS_LICENSE]

# Static instances. Weights share one family so Avalonia picks them by FontWeight.
ROBOTO_FACES = [("Roboto-Regular.ttf", 400, "Regular"), ("Roboto-Medium.ttf", 500, "Medium"), ("Roboto-Bold.ttf", 700, "Bold")]
ROBOTO_MONO_FACES = [("RobotoMono-Regular.ttf", 400, "Regular"), ("RobotoMono-Medium.ttf", 500, "Medium")]
SYMBOLS_FACES = [
    ("MaterialSymbolsRounded.ttf", 0, "Material Symbols Rounded"),
    ("MaterialSymbolsRoundedFilled.ttf", 1, "Material Symbols Rounded Filled"),
]

# Icons whose filled glyph must differ from the outline one.
FILLED_MUST_DIFFER = [
    "bookmark", "folder", "lock", "info", "check_circle", "error", "warning", "cancel", "save", "description",
]

# Characters compared against HarfBuzz for the text faces.
TEXT_SAMPLE = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,:;!?-_/\\()[]{}'\"@#%&*+=<>|~€é…"


def log(message: str) -> None:
    print(message, flush=True)


def fail(message: str) -> None:
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fetch(source: Source) -> Path:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    target = SOURCE_DIR / source.local_name
    if target.exists() and sha256_of(target) == source.sha256:
        return target
    log(f"downloading {source.repo}@{source.commit[:10]} {source.path}")
    request = urllib.request.Request(source.url, headers={"User-Agent": "savepoint-make-fonts"})
    with urllib.request.urlopen(request) as response:
        data = response.read()
    actual = hashlib.sha256(data).hexdigest()
    if actual != source.sha256:
        fail(f"{source.path}: SHA-256 is {actual}, expected {source.sha256}")
    target.write_bytes(data)
    return target


def read_icon_names() -> list[str]:
    names: list[str] = []
    for raw in ICONS_TXT.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line and not line.startswith("#"):
            names.append(line)
    duplicates = sorted({n for n in names if names.count(n) > 1})
    if duplicates:
        fail(f"icons.txt lists these names more than once: {', '.join(duplicates)}")
    return names


def read_codepoints(path: Path) -> dict[str, int]:
    points: dict[str, int] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            name, value = line.split()
            points[name] = int(value, 16)
    return points


def pascal_case(name: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in name.split("_"))


# --- Static instances ------------------------------------------------------------


def pin(variable: TTFont, location: dict[str, float]) -> TTFont:
    static = instancer.instantiateVariableFont(variable, location, inplace=False, updateFontNames=False)
    static.recalcTimestamp = False
    # The instancer leaves STAT behind, and its axis names point at name records we remove.
    for table in ("STAT", "fvar", "gvar", "avar", "cvar", "HVAR", "MVAR"):
        if table in static:
            del static[table]
    return static


def instantiate(source: Path, location: dict[str, float]) -> TTFont:
    return pin(TTFont(source, recalcTimestamp=False), location)


def run_subsetter(font: TTFont, unicodes: set[int], *, keep_layout: bool) -> None:
    options = subset.Options()
    # Static instances of a variable font keep its bytecode, which is tuned for the default master only.
    options.hinting = False
    options.glyph_names = False
    options.notdef_outline = True
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.name_languages = ["*"]
    options.recalc_bounds = True
    options.recalc_average_width = True
    if keep_layout:
        options.layout_features = ["*"]
    else:
        # Icons are addressed by code point, so the ligature tables are dead weight.
        options.layout_features = []
        options.layout_closure = False
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=unicodes)
    subsetter.subset(font)
    if not keep_layout:
        for table in ("GSUB", "GPOS", "GDEF"):
            if table in font:
                del font[table]


def add_smart_dropout_prep(font: TTFont) -> None:
    # Same program the upstream Roboto Mono and Material Symbols files carry.
    prep = newTable("prep")
    prep.program = ttProgram.Program()
    prep.program.fromAssembly(["PUSHW[]", "511", "SCANCTRL[]", "PUSHB[]", "4", "SCANTYPE[]"])
    font["prep"] = prep


def set_weight(font: TTFont, weight: int) -> None:
    os2 = font["OS/2"]
    os2.usWeightClass = weight
    selection = os2.fsSelection & ~0b1100001  # italic, bold, regular
    selection |= 0b100000 if weight >= 700 else 0b1000000
    os2.fsSelection = selection
    font["head"].macStyle = 0b1 if weight >= 700 else 0


def set_names(font: TTFont, *, family: str, style: str, ps_name: str) -> None:
    """RIBBI names in IDs 1/2 plus typographic names in 16/17, so every weight shares one family."""
    name_table = font["name"]
    version = (name_table.getDebugName(5) or "").split(";")[0].strip()
    replaced = {1, 2, 3, 4, 6, 16, 17, 21, 22, 25}
    name_table.names = [n for n in name_table.names if n.nameID < 256 and n.nameID not in replaced]

    ribbi_style = style if style in ("Regular", "Bold") else "Regular"
    ribbi_family = family if ribbi_style == style else f"{family} {style}"
    records = {
        1: ribbi_family,
        2: ribbi_style,
        3: f"{version};SavePoint;{ps_name}",
        4: f"{family} {style}",
        6: ps_name,
    }
    if ribbi_family != family:
        records[16] = family
        records[17] = style
    for name_id, value in records.items():
        name_table.setName(value, name_id, 3, 1, 0x409)


def build_text_face(source: Path, location: dict[str, float], *, family: str, ps_prefix: str, weight: int, style: str) -> TTFont:
    font = instantiate(source, location)
    set_weight(font, weight)
    run_subsetter(font, set(font.getBestCmap().keys()), keep_layout=True)
    add_smart_dropout_prep(font)
    set_names(font, family=family, style=style, ps_name=f"{ps_prefix}-{style}")
    return font


# --- Material Symbols ------------------------------------------------------------


def effective_substitutions(variable: TTFont, normalized: dict[int, float]) -> dict[str, str]:
    """Single-glyph swaps GSUB applies at a location, resolving FeatureVariations.

    Material Symbols reaches its filled shapes partly by swapping glyphs (name -> name.fill)
    through a FeatureVariations record that fires near FILL=1. Pinning FILL in the instancer
    interpolates outlines but does not apply that swap to the cmap, so it has to be resolved here
    before the layout tables are dropped.
    """
    gsub = variable["GSUB"].table
    lookups = gsub.LookupList.Lookup
    feature_lookups = {i: list(f.Feature.LookupListIndex) for i, f in enumerate(gsub.FeatureList.FeatureRecord)}

    variations = getattr(gsub, "FeatureVariations", None)
    if variations is not None:
        for record in variations.FeatureVariationRecord:
            conditions = record.ConditionSet.ConditionTable
            if all(c.FilterRangeMinValue <= normalized.get(c.AxisIndex, 0.0) <= c.FilterRangeMaxValue for c in conditions):
                for substitution in record.FeatureTableSubstitution.SubstitutionRecord:
                    feature_lookups[substitution.FeatureIndex] = list(substitution.Feature.LookupListIndex)
                break

    used = sorted({index for indices in feature_lookups.values() for index in indices})
    mappings: list[dict[str, str]] = []
    for index in used:
        lookup = lookups[index]
        for subtable in lookup.SubTable:
            inner = subtable.ExtSubTable if lookup.LookupType == 7 else subtable
            kind = type(inner).__name__
            if kind == "SingleSubst":
                mappings.append(dict(inner.mapping))
            elif kind != "LigatureSubst":
                fail(f"unexpected GSUB subtable {kind} in lookup {index}; the glyph-swap handling needs review")

    swaps: dict[str, str] = {}
    for mapping in mappings:
        for source, target in mapping.items():
            swaps[source] = target
    return swaps


def build_symbols_face(source: Path, fill: float, *, family: str, codepoints: set[int]) -> TTFont:
    variable = TTFont(source, recalcTimestamp=False)
    axes = [a.axisTag for a in variable["fvar"].axes]
    avar = variable["avar"].segments
    for axis in ("FILL", "GRAD", "opsz"):
        if axis in avar and avar[axis] != {-1.0: -1.0, 0.0: 0.0, 1.0: 1.0}:
            fail(f"avar maps {axis} non-linearly; the FILL swap lookup assumes normalized == user value")
    normalized = {axes.index(axis): 0.0 for axis in axes}
    normalized[axes.index("FILL")] = fill
    swaps = effective_substitutions(variable, normalized)

    font = pin(variable, {"FILL": fill, "GRAD": 0, "opsz": 24, "wght": 400})

    best = font.getBestCmap()
    swapped = sum(1 for codepoint in codepoints if best.get(codepoint) in swaps)
    for table in font["cmap"].tables:
        for codepoint in codepoints:
            glyph = table.cmap.get(codepoint)
            if glyph in swaps:
                table.cmap[codepoint] = swaps[glyph]
    log(f"  FILL={fill:g}: {swapped} of {len(codepoints)} icons use a swapped glyph")

    set_weight(font, 400)
    run_subsetter(font, codepoints, keep_layout=False)
    add_smart_dropout_prep(font)
    set_names(font, family=family, style="Regular", ps_name=f"{family.replace(' ', '')}-Regular")
    return font


# --- Output and checks -------------------------------------------------------------


def glyph_bytes(font: TTFont, codepoint: int) -> bytes:
    glyf = font["glyf"]
    return glyf[font.getBestCmap()[codepoint]].compile(glyf)


def save(font: TTFont, name: str) -> Path:
    path = STAGE_DIR / name
    font.save(path)
    return path


def copy_license(source: Path, name: str) -> None:
    text = source.read_text(encoding="utf-8").replace("\r\n", "\n")
    (OUTPUT_DIR / name).write_text(text, encoding="utf-8", newline="\n")


def write_icons_cs(names: list[str], points: dict[str, int]) -> None:
    entries: dict[str, int] = {}
    for name in names:
        const = pascal_case(name)
        if not re.fullmatch(r"[A-Za-z][A-Za-z0-9]*", const):
            fail(f"icon name {name!r} does not make a valid C# identifier ({const!r})")
        if const in entries:
            fail(f"icon names collide on the C# identifier {const!r}")
        entries[const] = points[name]

    def literal(codepoint: int) -> str:
        return f"\\u{codepoint:04x}" if codepoint <= 0xFFFF else f"\\U{codepoint:08x}"

    lines = [
        "// <auto-generated>",
        "// Generated by build/fonts/make-fonts.py from build/fonts/icons.txt. Do not edit by hand.",
        "// </auto-generated>",
        "",
        "namespace Savepoint.App.Theme;",
        "",
        "// Code points rather than ligature names: the bundled icon fonts carry no ligature tables.",
        "public static class Icons",
        "{",
    ]
    for const in sorted(entries):
        lines.append(f'    public const string {const} = "{literal(entries[const])}";')
    lines += ["}", ""]
    ICONS_CS.parent.mkdir(parents=True, exist_ok=True)
    ICONS_CS.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def harfbuzz_font(path: Path, location: dict[str, float]) -> "hb.Font":
    font = hb.Font(hb.Face(hb.Blob.from_file_path(str(path))))
    font.set_variations(location)
    return font


def shape_to_glyph(font: "hb.Font", text: str) -> int:
    buffer = hb.Buffer()
    buffer.add_str(text)
    buffer.guess_segment_properties()
    hb.shape(font, buffer)
    glyphs = [info.codepoint for info in buffer.glyph_infos]
    if len(glyphs) != 1:
        fail(f"HarfBuzz shaped {text!r} to {len(glyphs)} glyphs, expected 1")
    return glyphs[0]


def measure_with_harfbuzz(font: "hb.Font", glyph: int) -> tuple[float, tuple | None, int]:
    area, bounds = AreaPen(None), BoundsPen(None)
    font.draw_glyph_with_pen(glyph, area)
    font.draw_glyph_with_pen(glyph, bounds)
    return abs(area.value), bounds.bounds, font.get_glyph_h_advance(glyph)


def measure_output(font: TTFont, codepoint: int) -> tuple[float, tuple | None, int]:
    name = font.getBestCmap()[codepoint]
    glyph_set = font.getGlyphSet()
    area, bounds = AreaPen(glyph_set), BoundsPen(glyph_set)
    glyph_set[name].draw(area)
    glyph_set[name].draw(bounds)
    return abs(area.value), bounds.bounds, font["hmtx"][name][0]


def matches(expected: tuple, actual: tuple) -> bool:
    expected_area, expected_bounds, expected_advance = expected
    actual_area, actual_bounds, actual_advance = actual
    if abs(expected_advance - actual_advance) > 1:
        return False
    if (expected_bounds is None) != (actual_bounds is None):
        return False
    if expected_bounds is not None and any(abs(e - a) > 3 for e, a in zip(expected_bounds, actual_bounds)):
        return False
    # Instancing rounds points to integers, so allow a small relative error in the area.
    return abs(expected_area - actual_area) <= max(150.0, 0.01 * max(expected_area, actual_area))


def check_against_harfbuzz(label: str, source: Path, location: dict[str, float], output: Path, items: list[tuple[str, int]]) -> None:
    """Shape each text with HarfBuzz on the upstream variable font and compare the glyph it draws with ours.

    HarfBuzz applies the font's own GSUB (ligatures and the FILL glyph swaps), so this is
    independent of how the build resolved them.
    """
    reference = harfbuzz_font(source, location)
    font = TTFont(output)
    wrong = [
        text for text, codepoint in items
        if not matches(measure_with_harfbuzz(reference, shape_to_glyph(reference, text)), measure_output(font, codepoint))
    ]
    if wrong:
        fail(f"{label}: glyphs differ from HarfBuzz on the upstream font for {', '.join(repr(t) for t in wrong[:12])}"
             f"{' ...' if len(wrong) > 12 else ''}")


def verify_against_upstream(paths: dict[str, Path], names: list[str], points: dict[str, int]) -> None:
    for file_name, fill, _ in SYMBOLS_FACES:
        check_against_harfbuzz(
            file_name, paths[SYMBOLS.local_name], {"FILL": fill, "GRAD": 0, "opsz": 24, "wght": 400},
            STAGE_DIR / file_name, [(n, points[n]) for n in names])
    sample = [(c, ord(c)) for c in TEXT_SAMPLE]
    for file_name, weight, _ in ROBOTO_FACES:
        check_against_harfbuzz(
            file_name, paths[ROBOTO.local_name], {"wght": weight, "wdth": 100}, STAGE_DIR / file_name, sample)
    for file_name, weight, _ in ROBOTO_MONO_FACES:
        check_against_harfbuzz(
            file_name, paths[ROBOTO_MONO.local_name], {"wght": weight}, STAGE_DIR / file_name, sample)


def verify_outputs(names: list[str], points: dict[str, int]) -> None:
    wanted = {points[n] for n in names}
    for file_name, _, family in SYMBOLS_FACES:
        font = TTFont(STAGE_DIR / file_name)
        for table in ("fvar", "gvar", "STAT", "GSUB", "GPOS"):
            if table in font:
                fail(f"{file_name} still has a {table} table")
        mapped = set(font.getBestCmap().keys())
        if mapped != wanted:
            fail(f"{file_name}: cmap differs from icons.txt (missing {sorted(wanted - mapped)}, extra {sorted(mapped - wanted)})")
        empty = [n for n in names if font["glyf"][font.getBestCmap()[points[n]]].numberOfContours == 0]
        if empty:
            fail(f"{file_name}: empty glyphs for {', '.join(empty)}")
        if font["name"].getDebugName(1) != family:
            fail(f"{file_name}: family name is {font['name'].getDebugName(1)!r}")

    outline = TTFont(STAGE_DIR / SYMBOLS_FACES[0][0])
    filled = TTFont(STAGE_DIR / SYMBOLS_FACES[1][0])
    differing = [n for n in names if glyph_bytes(outline, points[n]) != glyph_bytes(filled, points[n])]
    log(f"  {len(differing)} of {len(names)} icons differ between outline and filled")
    unchanged = [n for n in FILLED_MUST_DIFFER if n not in differing]
    if unchanged:
        fail(f"filled font draws the outline glyph for {', '.join(unchanged)}")

    for file_name, weight, _ in ROBOTO_FACES + ROBOTO_MONO_FACES:
        font = TTFont(STAGE_DIR / file_name)
        for table in ("fvar", "gvar", "avar", "STAT", "HVAR"):
            if table in font:
                fail(f"{file_name} still has a {table} table")
        if font["OS/2"].usWeightClass != weight:
            fail(f"{file_name}: usWeightClass is {font['OS/2'].usWeightClass}")


def main() -> None:
    if fontTools.version != REQUIRED_FONTTOOLS:
        fail(f"fontTools {REQUIRED_FONTTOOLS} is required for reproducible output, found {fontTools.version}")

    paths = {source.local_name: fetch(source) for source in ALL_SOURCES}

    names = read_icon_names()
    points = read_codepoints(paths[SYMBOLS_CODEPOINTS.local_name])
    unknown = [n for n in names if n not in points]
    if unknown:
        fail(f"not in the Material Symbols code points: {', '.join(unknown)}")

    # Build and verify in a staging folder so a failed check never leaves bad fonts in the source tree.
    shutil.rmtree(STAGE_DIR, ignore_errors=True)
    STAGE_DIR.mkdir(parents=True)
    staged: list[Path] = []

    for file_name, weight, style in ROBOTO_FACES:
        font = build_text_face(
            paths[ROBOTO.local_name], {"wght": weight, "wdth": 100},
            family="Roboto", ps_prefix="Roboto", weight=weight, style=style)
        staged.append(save(font, file_name))
    for file_name, weight, style in ROBOTO_MONO_FACES:
        font = build_text_face(
            paths[ROBOTO_MONO.local_name], {"wght": weight},
            family="Roboto Mono", ps_prefix="RobotoMono", weight=weight, style=style)
        staged.append(save(font, file_name))

    codepoints = {points[n] for n in names}
    for file_name, fill, family in SYMBOLS_FACES:
        font = build_symbols_face(paths[SYMBOLS.local_name], float(fill), family=family, codepoints=codepoints)
        staged.append(save(font, file_name))

    verify_outputs(names, points)
    verify_against_upstream(paths, names, points)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for stale in OUTPUT_DIR.glob("*.ttf"):
        if stale.name not in {path.name for path in staged}:
            stale.unlink()
    outputs = [Path(shutil.copy2(path, OUTPUT_DIR / path.name)) for path in staged]
    copy_license(paths[ROBOTO_OFL.local_name], "Roboto-OFL.txt")
    copy_license(paths[ROBOTO_MONO_OFL.local_name], "RobotoMono-OFL.txt")
    copy_license(paths[SYMBOLS_LICENSE.local_name], "MaterialSymbols-LICENSE.txt")
    write_icons_cs(names, points)

    log("")
    total = 0
    for path in outputs:
        size = path.stat().st_size
        total += size
        log(f"{size:>9,} bytes  {path.relative_to(ROOT).as_posix()}")
    log(f"{total:>9,} bytes  total ({len(outputs)} fonts, {len(names)} icons)")
    log(f"wrote {ICONS_CS.relative_to(ROOT).as_posix()}")


if __name__ == "__main__":
    main()
