"""Minimal preview.21 clock adaptation of frozen report08 B; preserve old evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'src/platform/x68k/champon8_poly_demo/build/boss-animation-20260917/verify-1789593386315951000/choice-1'
OUT = ROOT / 'src/platform/x68k/champon8_poly_demo/build/clock-fix-20260918'
ASM = Path('D:/work/DevelopTools/Assemblers/m68kasm/dist/m68kasm-0.9.0-preview.1-win-x64/bin/m68kasm.exe')
OLD_HASH = 'a0d86c915078a11df3e47ba083b1944b47b2bf9b73fbd7f676bfed979ea97f70'
REPORT_ID = '20260918-x68000-clock-fix-10'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def assemble(source, binary):
    result = subprocess.run([str(ASM), 'assemble', str(source), '-o', str(binary),
                             '--cpu', '68000', '--base-address', '0x1000'],
                            capture_output=True, text=True, encoding='utf8')
    binary.with_suffix('.assembler.txt').write_text(result.stdout + result.stderr, 'utf8')
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return {k: int(v, 16) for k, v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)', result.stdout, re.M)}


def build(output):
    output.mkdir(parents=True, exist_ok=False)
    old = (BASE / 'guest.bin').read_bytes()
    assert sha(old) == OLD_HASH
    original = (BASE / 'guest.m68').read_text('utf8')
    baseline_symbols = assemble(BASE / 'guest.m68', output / 'rebuilt08.bin')
    assert (output / 'rebuilt08.bin').read_bytes() == old, 'Frozen source must reproduce the deployed BIN'
    replacements = [('move.w #$0100,$e80028.l', 'move.w #$0115,$e80028.l'),
                    ('btst #7,MFP_GPIP.l', 'btst #4,MFP_GPIP.l')]
    source = original
    for before, after in replacements:
        assert source.count(before) == 1, before
        source = source.replace(before, after)
    (output / 'guest.m68').write_text(source, 'utf8')
    symbols = assemble(output / 'guest.m68', output / 'guest.bin')
    assert symbols == baseline_symbols, 'No code/data relocation is expected'
    new = (output / 'guest.bin').read_bytes()
    assert len(new) == len(old)
    changes = [{'offset': i, 'address': 0x1000+i, 'before': a, 'after': b}
               for i, (a, b) in enumerate(zip(old, new)) if a != b]
    assert [(c['before'], c['after']) for c in changes] == [(0, 0x15), (7, 4)], changes
    m = json.loads((BASE / 'build.json').read_text('utf8'))
    dependencies = [BASE / 'guest.m68'] + [Path(p) for p in re.findall(r'^include "([^"]+)"', original, re.M)]
    m.update(reportId=REPORT_ID, schema='x68000-preview21-clock-fix.v1',
             supersedesReportId='20260918-x68000-timing21-acceptance-01',
             binarySha256=sha(new), sourceSha256=sha(source.encode()), symbols=symbols,
             baselineBinarySha256=OLD_HASH, byteChanges=changes,
             changes=['R20 0x0100 -> 0x0115: retain 256 colors and restore 512 scan timing',
                      'MFP GPIP bit7 -> bit4: use V-DISP falling edge, not horizontal sync'],
             preserved=['player 2 units per logical tick', 'all movement/animation formulas',
                        '832 dwell ticks', 'palette/models/renderer', 'all cache/compositor optimizations'],
             knownRemaining=['white low-resolution player/enemies', 'normal keyboard adapter absent',
                             'hardware timing not verified'],
             inputDependencies=[{'path': str(p), 'sha256': sha(p.read_bytes())} for p in dependencies],
             clockFixBuilderSha256=sha(Path(__file__).read_bytes()), assemblerSha256=sha(ASM.read_bytes()))
    (output / 'build.json').write_text(json.dumps(m, indent=2), 'utf8')
    return m


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--output', type=Path, default=OUT / 'candidate')
    a = ap.parse_args()
    m = build(a.output.resolve())
    print(json.dumps({'binarySha256': m['binarySha256'], 'byteChanges': m['byteChanges']}))
