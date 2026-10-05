#!/usr/bin/env python3
"""Build the X68000 Champon8 RAM-entry polygon demo from pinned local assets."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / "src" / "platform" / "x68k" / "champon8_poly_demo"
DEFAULT_MODEL_READER = Path(
    r"D:\work\PC8001mkⅡで何か動かそう\ports\starcruiser-dual-demo\tools\model_reference.py"
)
DEFAULT_ASSEMBLER = Path(
    r"D:\work\DevelopTools\Assemblers\m68kasm\dist\m68kasm-0.9.0-preview.1-win-x64\bin\m68kasm.exe"
)
EXPECTED_ROM_SHA256 = "662a3f4c1ae358a208dea139eb2ff4c9608d93cf94381ea22e3f4b38d21401e3"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_assets(reader_path: Path) -> tuple[dict, Path]:
    spec = importlib.util.spec_from_file_location("starcruiser_model_reference", reader_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load model reader: {reader_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    assets = module.assets()
    rom_path = Path(module.PORT) / "reference" / "Star Cruiser (Japan).md"
    rom = rom_path.read_bytes()
    actual = sha256(rom)
    if actual != EXPECTED_ROM_SHA256:
        raise RuntimeError(f"Reference ROM hash mismatch: {actual}")
    if len(assets["vertices"]) != 35 or len(assets["faces"]) != 58:
        raise RuntimeError("Expected model 0x1c with 35 vertices and 58 triangles")
    # The model's nine material bits are GGGRRRBBB, not a display palette index.
    # Bind their channel order to the original colour lookup and MD CRAM palette.
    for word, pattern, index, cram in (
        (0x007, 0x1111, 1, 0xE00),
        (0x038, 0x2222, 2, 0x00E),
        (0x1C0, 0x4444, 4, 0x0E0),
    ):
        lookup = int.from_bytes(rom[0x461A + word * 2 : 0x461C + word * 2], "big")
        palette = int.from_bytes(rom[0x5BC + index * 2 : 0x5BE + index * 2], "big")
        if lookup != pattern or palette != cram:
            raise RuntimeError("Original material colour lookup/palette changed")
    if len(assets["colorWords"]) != 10 or any(not 0 <= word <= 0x1FF for word in assets["colorWords"]):
        raise RuntimeError("Expected ten original nine-bit material colours")
    return assets, rom_path


def x68_color(red: int, green: int, blue: int, source_max: int = 255) -> int:
    """Quantize RGB to GRB555+shared-I using the smallest total channel error."""
    if source_max <= 0 or any(not 0 <= value <= source_max for value in (red, green, blue)):
        raise ValueError("RGB components must be within the source range")
    candidates = []
    for intensity in (0, 1):
        values = [
            max(0, min(31, (value * 63 - intensity * source_max + source_max) // (2 * source_max)))
            for value in (red, green, blue)
        ]
        error = sum(
            (((component * 2 + intensity) * source_max) - source * 63) ** 2
            for component, source in zip(values, (red, green, blue))
        )
        r5, g5, b5 = values
        candidates.append((error, (g5 << 11) | (r5 << 6) | (b5 << 1) | intensity))
    return min(candidates)[1]


STAR_RGB = (
    (55, 90, 150),
    (80, 150, 220),
    (165, 220, 255),
    (255, 255, 255),
)


def material_palette(assets: dict) -> list[dict]:
    result = []
    for index, word in enumerate(assets["colorWords"], 1):
        rgb3 = [(word >> 3) & 7, (word >> 6) & 7, word & 7]
        result.append({
            "paletteIndex": index,
            "sourceColorWord": word,
            "sourceRgb3": rgb3,
            "linearRgb8": [round(value * 255 / 7) for value in rgb3],
            "x68000ColorWord": x68_color(*rgb3, source_max=7),
        })
    return result


def palette_words(assets: dict) -> list[int]:
    return (
        [0]
        + [material["x68000ColorWord"] for material in material_palette(assets)]
        + [x68_color(*rgb) for rgb in STAR_RGB]
        + [0x0842]  # Preserve the original faint gray reticle.
    )


def rows(values: list[int], count: int = 12) -> list[list[int]]:
    return [values[i : i + count] for i in range(0, len(values), count)]


def emit_model(assets: dict) -> str:
    vertices = [value for vertex in assets["vertices"] for value in vertex]
    colors = palette_words(assets)
    if len(colors) != 16:
        raise RuntimeError("The indexed graphics mode requires exactly 16 palette words")
    faces: list[int] = []
    for a, b, c, material in assets["faces"]:
        if not 1 <= material <= 10:
            raise RuntimeError(f"Unexpected material index: {material}")
        faces.extend((a, b, c, material))
    lines = [
        "// Generated from local hash-pinned Star Cruiser model 0x1c; do not commit.",
        "dirty_span_left:",
    ]
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows([0] * 1024))
    lines.append("dirty_span_right:")
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows([0] * 1024))
    lines.extend([
        "projected_vertices:",
    ])
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows([0] * (35 * 3)))
    lines.append("face_order:")
    lines.extend("  defl " + ",".join(map(str, row)) for row in rows([0] * 58, 8))
    lines.extend([
        "model_vertices:",
    ])
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows(vertices))
    lines.append("model_faces:")
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows(faces, 8))
    lines.append("sine_table:")
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows(assets["sine"], 8))
    lines.append("palette_words:")
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows(colors, 8))
    stars: list[int] = []
    state = 0x68000
    used: set[tuple[int, int]] = set()
    while len(used) < 96:
        state = (state * 1103515245 + 12345) & 0x7FFFFFFF
        x = (state >> 7) & 0x1FF
        state = (state * 1103515245 + 12345) & 0x7FFFFFFF
        y = (state >> 7) & 0x1FF
        if (x, y) in used or abs(x - 256) < 6 or abs(y - 256) < 6:
            continue
        used.add((x, y))
        stars.extend((x, y, 11 + ((state >> 20) & 3)))
    lines.append("starfield:")
    lines.extend("  defw " + ",".join(map(str, row)) for row in rows(stars, 12))
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-reader", type=Path, default=DEFAULT_MODEL_READER)
    parser.add_argument("--assembler", type=Path, default=DEFAULT_ASSEMBLER)
    args = parser.parse_args()

    build = DEMO / "build"
    build.mkdir(parents=True, exist_ok=True)
    assets, rom_path = load_assets(args.model_reader.resolve())
    generated = emit_model(assets)
    include_path = build / "model_generated.inc"
    include_path.write_text(generated, encoding="utf-8", newline="\n")
    output = build / "x68000-starcruiser-512.bin"

    command = [
        str(args.assembler.resolve()),
        "assemble",
        str(DEMO / "demo.m68"),
        "-o",
        str(output),
        "--cpu",
        "68000",
        "--base-address",
        "0x1000",
    ]
    completed = subprocess.run(
        command,
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
    )
    if completed.returncode:
        sys.stderr.write(completed.stdout)
        sys.stderr.write(completed.stderr)
        return completed.returncode

    symbols = {
        name: int(value, 16)
        for name, value in re.findall(
            r"^\s+(\S+) = \$([0-9a-fA-F]+)$", completed.stdout, flags=re.MULTILINE
        )
    }
    required_symbols = (
        "demo_data", "current_angle", "dirty_span_left", "dirty_span_right",
        "present_frame", "clear_previous_spans", "draw_backdrop", "draw_faces",
        "draw_page", "draw_vram_base", "dirty_left_pointer", "dirty_right_pointer",
    )
    missing_symbols = [name for name in required_symbols if name not in symbols]
    if missing_symbols:
        raise RuntimeError(f"Assembler did not report required symbols: {missing_symbols}")

    binary = output.read_bytes()
    manifest = {
        "schema": "x68000.champon8-polygon-demo.v1",
        "loadAddress": 0x1000,
        "entryPc": 0x1000,
        "stackPointer": 0xFF000,
        "screen": {
            "width": 512, "height": 512, "pixelAspect": "1:1", "focalX": 700, "focalY": 700,
            "colorMode": "16-color indexed", "graphicsPages": [0, 1],
            "paletteWords": palette_words(assets),
        },
        "renderer": {
            "cpu": "MC68000",
            "rotation": "runtime Q14 yaw+pitch",
            "projection": "signed integer perspective",
            "culling": "signed 32-bit screen cross",
            "ordering": "stable far-to-near bubble painter",
            "raster": "top-inclusive bottom-exclusive scanline spans",
            "clear": "startup full-page MOVEM on both pages; steady per-page previous-Y-range per-scanline dirty union",
            "spanWrites": "inline 32-bit indexed pixel pairs with a 16-bit tail",
            "presentation": "draw into hidden graphics page; switch complete page at VBlank",
            "buffering": "two graphics pages with independent 512-line dirty-span histories",
        },
        "symbols": {name: symbols[name] for name in required_symbols},
        "model": {
            "name": "Star Cruiser model 0x1c",
            "vertices": len(assets["vertices"]),
            "triangles": len(assets["faces"]),
            "referenceRom": str(rom_path),
            "referenceRomSha256": EXPECTED_ROM_SHA256,
            "generatedIncludeSha256": sha256(generated.encode("utf-8")),
            "materialColorSource": "original GGGRRRBBB words; ROM lookup 0x461A and CRAM palette 0x5BC axis checks",
            "materialPalette": material_palette(assets),
            "distribution": "local-only; generated model data is not tracked",
        },
        "binary": {"path": str(output), "bytes": len(binary), "sha256": sha256(binary)},
        "assembler": {
            "path": str(args.assembler.resolve()),
            "sha256": sha256(args.assembler.resolve().read_bytes()),
        },
    }
    (build / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
