"""Player-only 1.5x movement tuning; keep corrected clock and other actors unchanged."""
import argparse
import json
from pathlib import Path

from build_champon8_clock_fix import ROOT, assemble, sha

BASE_RUN = ROOT / 'src/platform/x68k/champon8_poly_demo/build/clock-fix-20260918/verify-1789698053920466700'
OUT = ROOT / 'src/platform/x68k/champon8_poly_demo/build/player-speed-20260918'
REPORT_ID = '20260918-x68000-player-speed-11'
BASE_HASH = '72a694869b0f04d4b21dfda14fd3e0b5ed9f34187701ede793d62f5ac102e257'
PLAYER_STEP = 3


def build(output):
    output.mkdir(parents=True, exist_ok=False)
    base = BASE_RUN / 'candidate'
    old = (base / 'guest.bin').read_bytes()
    assert sha(old) == BASE_HASH
    m = json.loads((base / 'build.json').read_text('utf8'))
    assert m['binarySha256'] == BASE_HASH
    original = (base / 'guest.m68').read_text('utf8')
    symbols = assemble(base / 'guest.m68', output / 'rebuilt10.bin')
    assert (output / 'rebuilt10.bin').read_bytes() == old
    start, end = original.index('[input_service\n'), original.index('\n[exercise_script\n')
    body = original[start:end]
    for opcode, reg in [('subq', 'd2'), ('addq', 'd2'), ('subq', 'd3'), ('addq', 'd3')]:
        before = f'{opcode}.w #2,{reg}.w'
        assert body.count(before) == 1
        body = body.replace(before, f'{opcode}.w #{PLAYER_STEP},{reg}.w')
    source = original[:start] + body + original[end:]
    (output / 'guest.m68').write_text(source, 'utf8')
    assert assemble(output / 'guest.m68', output / 'guest.bin') == symbols
    new = (output / 'guest.bin').read_bytes()
    assert len(old) == len(new)
    changes = [{'offset': i, 'address': i+0x1000, 'before': a, 'after': b}
               for i, (a, b) in enumerate(zip(old, new)) if a != b]
    assert len(changes) == 4 and all(c['after'] - c['before'] == 2 for c in changes)
    assert all(symbols['input_service'] <= c['address'] < symbols['exercise_script'] for c in changes)
    m.update(reportId=REPORT_ID, schema='x68000-player-speed.v1', supersedesReportId='20260918-x68000-clock-fix-10',
             binarySha256=sha(new), baselineBinarySha256=BASE_HASH, sourceSha256=sha(source.encode()),
             symbols=symbols, byteChanges=changes, playerUnitsPerTick=PLAYER_STEP, previousPlayerUnitsPerTick=2,
             playerSpeedRatio=1.5, changes=['Player directional movement 2 -> 3 units per vertical tick'],
             preserved=['55.4577Hz V-DISP clock', 'CRTC R20=0x0115', 'enemy and projectile movement',
                        '832-tick boss dwell', 'palette/models/renderer', 'boundary clamps'],
             replayNote='Guest replay direction still changes every64 ticks; greater movement also broadens its travel range',
             speedBuilderSha256=sha(Path(__file__).read_bytes()),
             parentBuild={'path': str(base / 'build.json'), 'sha256': sha((base / 'build.json').read_bytes())})
    (output / 'build.json').write_text(json.dumps(m, indent=2), 'utf8')
    return m


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--output', type=Path, default=OUT / 'candidate')
    m = build(ap.parse_args().output.resolve())
    print(json.dumps({'binarySha256': m['binarySha256'], 'byteChanges': m['byteChanges']}))
