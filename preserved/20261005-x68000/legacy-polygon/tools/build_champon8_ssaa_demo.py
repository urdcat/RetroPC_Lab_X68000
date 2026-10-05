#!/usr/bin/env python3
"""Build genuine 512-square -> 256-square four-sample MC68000 SSAA demo.

Only constant colour blends are precomputed. Vertices, sample coverage,
occlusion, rasterization and 2x2 resolve all execute on the guest MC68000.
Generated reference data and binaries remain in ignored build/ssaa.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import struct
import subprocess
import sys

sys.dont_write_bytecode = True
from build_champon8_polygon_demo import (
    ROOT, DEMO, DEFAULT_ASSEMBLER, DEFAULT_MODEL_READER, EXPECTED_ROM_SHA256,
    emit_model, load_assets, material_palette, palette_words, rows, sha256,
)


def rgb6(word: int) -> tuple[int, int, int]:
    return tuple(((word >> shift) & 31) * 2 + (word & 1) for shift in (6, 11, 1))


def quantize_average(sums: list[int]) -> int:
    """Nearest RGB6 shared-I encoding; exact ties choose the smaller word."""
    candidates = []
    for intensity in (0, 1):
        levels = [max(0, min(31, (value - 4 * intensity + 3) // 8)) for value in sums]
        error = sum((4 * (2 * level + intensity) - value) ** 2
                    for level, value in zip(levels, sums))
        red, green, blue = levels
        candidates.append((error, (green << 11) | (red << 6) | (blue << 1) | intensity))
    return min(candidates)[1]


def make_lookup(palette: list[int]) -> bytes:
    """High byte is the top pair, low byte the bottom pair; even X is high nibble."""
    colors = [rgb6(word) for word in palette]
    result = bytearray()
    for pattern in range(65536):
        samples = [colors[(pattern >> shift) & 15] for shift in (12, 8, 4, 0)]
        # sum/4 in the native six-bit DAC domain, without rounding samples.
        sums = [sum(sample[channel] for sample in samples) for channel in range(3)]
        result.extend(struct.pack(">H", quantize_average(sums)))
    return bytes(result)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembler", type=Path, default=DEFAULT_ASSEMBLER)
    parser.add_argument("--model-reader", type=Path, default=DEFAULT_MODEL_READER)
    args = parser.parse_args()
    build = DEMO / "build" / "ssaa"
    build.mkdir(parents=True, exist_ok=True)
    assets, rom_path = load_assets(args.model_reader.resolve())
    palette = palette_words(assets)
    generated = emit_model(assets)
    generated += "solid_patterns:\n  defl " + ",".join(f"${i * 0x11111111:08x}" for i in range(16)) + "\n"
    generated += "solid_output_patterns:\n  defl " + ",".join(f"${word * 0x10001:08x}" for word in palette) + "\n"
    for name in ("resolve_span_left", "resolve_span_right"):
        generated += name + ":\n"
        generated += "\n".join("  defw " + ",".join(map(str, row)) for row in rows([0] * 256)) + "\n"
    (build / "model_generated.inc").write_text(generated, encoding="utf-8", newline="\n")
    lookup = make_lookup(palette)
    lookup_path = build / "aa_lookup.bin"
    lookup_path.write_bytes(lookup)
    output = build / "x68000-starcruiser-ssaa.bin"
    completed = subprocess.run([
        str(args.assembler.resolve()), "assemble", str(DEMO / "ssaa.m68"),
        "-o", str(output), "--cpu", "68000", "--base-address", "0x1000",
    ], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if completed.returncode:
        sys.stderr.write(completed.stdout + completed.stderr)
        return completed.returncode
    symbols = {name: int(value, 16) for name, value in re.findall(
        r"^\s+(\S+) = \$([0-9a-fA-F]+)$", completed.stdout, flags=re.MULTILINE)}
    required = ("demo_data", "current_angle", "draw_page", "draw_vram_base",
                "output_vram_base", "clear_previous_spans", "draw_backdrop", "draw_faces",
                "resolve_aa", "present_frame", "dirty_span_left", "dirty_span_right")
    if any(name not in symbols for name in required):
        raise RuntimeError("Missing guest symbols: " + str([n for n in required if n not in symbols]))
    binary = output.read_bytes()
    if 0x1000 + len(binary) >= 0x20000:
        raise RuntimeError("Code/data overlaps packed sample buffer")
    manifest = {
        "schema": "x68000.champon8-ssaa-demo.v1", "loadAddress": 0x1000,
        "entryPc": 0x1000, "stackPointer": 0xFF000,
        "screen": {"width": 256, "height": 256, "pixelAspect": "1:1",
                   "internalWidth": 512, "internalHeight": 512,
                   "samplesPerPixel": 4, "sampleArrangement": "2x2 box average, not decimation",
                   "colorMode": "GRB555+shared-I direct 16-bit",
                   "paletteWords": palette, "focalX": 700, "focalY": 700,
                   "crtcMode": 0x0310, "outputPageY": [0, 256], "vramStrideBytes": 1024},
        "renderer": {"cpu": "MC68000", "geometry": "same runtime Q14 yaw+pitch, 64 poses as native baseline",
                     "sampleBuffers": [0x20000, 0x60000], "sampleBufferBytes": 131072,
                     "sampleFormat": "packed 4bpp, even X in high nibble, 256-byte row",
                     "clear": "per-page previous silhouette only",
                     "resolve": "lookup of four actual colour samples; union of previous/current dirty spans",
                     "presentation": "VBlank page-0 Y scroll between complete 256-line output buffers",
                     "hotLoopSpanPixelCounters": "disabled; STATUS_SPANS/STATUS_PIXELS remain zero",
                     "precomputedImages": False},
        "symbols": symbols,
        "model": {"name": "Star Cruiser model 0x1c", "vertices": 35, "triangles": 58,
                  "referenceRom": str(rom_path), "referenceRomSha256": EXPECTED_ROM_SHA256,
                  "materialPalette": material_palette(assets), "distribution": "local-only generated reference data"},
        "binary": {"path": str(output), "bytes": len(binary), "sha256": sha256(binary)},
        "lookup": {"path": str(lookup_path), "address": 0x40000, "bytes": len(lookup),
                   "sha256": sha256(lookup), "entries": 65536,
                   "domain": "arithmetic average in native RGB6 DAC domain; nearest shared-I encoding; ties choose smaller word"},
        "generatedIncludeSha256": sha256(generated.encode("utf-8")),
        "assembler": {"path": str(args.assembler.resolve()), "sha256": sha256(args.assembler.read_bytes())},
    }
    (build / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"binary": manifest["binary"], "lookup": manifest["lookup"], "screen": manifest["screen"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
