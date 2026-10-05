"""Bounded09 comparison: unchanged08 baseline, fair-cache, direct boss raster."""
import argparse
import json
from pathlib import Path
import re
import subprocess

import build_champon8_boss_animation as animation

v1 = animation.v1
OUT = v1.DEMO / 'build/direct-boss-20260917'


def build(output=OUT, choice=0, backend='direct'):
    assert backend in ['fair-cache', 'direct']
    output = output.resolve()
    base = animation.build(output / 'base08', choice=choice)
    source = (output / 'base08/guest.m68').read_text('utf8')

    def edit(name, old, new):
        nonlocal source
        body = v1.blocks(source)[name]
        assert old in body, (name, old)
        source = v1.replace_block(source, name, body.replace(old, new, 1))

    # Both candidates share this scheduler change. Geometry scratch is single
    # threaded: do not enter GVRAM raster while a RAM renderer is suspended.
    edit('generate_pose', '  move.l job_context.l,a6', '''  clr.w direct_in_pose.l
  cmpi.w #4,S_SCENE.l
  bne.w .pose_begin
  move.w #1,direct_in_pose.l
.pose_begin:
  move.l job_context.l,a6''')
    edit('generate_pose', '.done:\n]', '.done:\n  clr.w direct_in_pose.l\n]')
    edit('service', '.compose:\n', '''.compose:
  tst.w direct_in_pose.l
  bne.w .exit
  tst.w direct_drawing.l
  bne.w .exit
''')
    renderer = (output / 'base08/renderer.inc').read_text('utf8')
    if backend == 'direct':
        edit('generate_pose', '  bsr.w pose_clear', '''  cmpi.w #5,job_actor.l
  beq.w .no_bitmap_clear
  bsr.w pose_clear
.no_bitmap_clear:''')
        edit('generate_pose', '  bsr.w draw_faces\n  bsr.w cache_bounds\n  bsr.w encode_runs', '''  cmpi.w #5,job_actor.l
  beq.w .geometry_only
  bsr.w draw_faces
  bsr.w cache_bounds
  bsr.w encode_runs
  bra.w .raster_done
.geometry_only:
  bsr.w cache_bounds
  bsr.w direct_store_geometry
.raster_done:''')
        edit('composite_row', '.row_started:\n', '''.row_started:
  cmpa.l #SNAPS+80,a4
  bne.w .cached_row
  bsr.w direct_draw_boss
  bra.w .next_object
.cached_row:
''')
        raster = v1.blocks(renderer)['raster_triangle']
        old = '  move.w d6.w,d2.w\n  mulu.w render_stride.l,d2.l'
        new = '''  tst.w direct_drawing.l
  beq.w .ram_span
  bsr.w direct_span
  bra.w .next_row
.ram_span:
  move.w d6.w,d2.w
  mulu.w render_stride.l,d2.l'''
        assert old in raster
        renderer = v1.replace_block(renderer, 'raster_triangle', raster.replace(old, new, 1))
    for name in ['generate_pose', 'composite_row']:
        body = re.sub(r'\b(b[a-z]+)\.s ', r'\1.w ', v1.blocks(source)[name])
        source = v1.replace_block(source, name, body)
    (output / 'renderer.inc').write_text(renderer, 'utf8')
    source = source.replace((output / 'base08/renderer.inc').as_posix(), (output / 'renderer.inc').as_posix())
    extra = (v1.DEMO / 'direct_boss.m68').read_text('utf8')
    source = source.replace('include "' + (output / 'renderer.inc').as_posix() + '"',
                            extra + '\ninclude "' + (output / 'renderer.inc').as_posix() + '"')
    (output / 'guest.m68').write_text(source, 'utf8')
    r = subprocess.run([str(v1.cache.ASM), 'assemble', str(output / 'guest.m68'), '-o',
                        str(output / 'guest.bin'), '--cpu', '68000', '--base-address', '0x1000'],
                       capture_output=True, text=True, encoding='utf8')
    (output / 'assembler.txt').write_text(r.stdout + r.stderr, 'utf8')
    if r.returncode:
        raise RuntimeError(r.stdout + r.stderr)
    symbols = {k: int(v, 16) for k, v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)', r.stdout, re.M)}
    assert (output / 'guest.bin').stat().st_size + 0x1000 < 0x8000
    m = {**base, 'schema': 'x68000-direct-boss-comparison.v1', 'backend': backend,
         'reportId': '20260917-x68000-direct-gvram-comparison-09', 'symbols': symbols,
         'directLogicSha256': v1.sha(v1.DEMO / 'direct_boss.m68'),
         'directBuilderSha256': v1.sha(Path(__file__)), 'base08Sha256': base['binarySha256'],
         'binarySha256': v1.sha(output / 'guest.bin'),
         'directGvramComparison': 'boss only; self/shots retain cached runs and same damage engine',
         'bossBuffer': '438-byte projected vertices/face order/count, no bitmap' if backend == 'direct' else 'unchanged08 bitmap'}
    (output / 'build.json').write_text(json.dumps(m, indent=2), 'utf8')
    print(json.dumps({'backend': backend, 'choice': choice, 'bytes': (output / 'guest.bin').stat().st_size,
                      'sha256': m['binarySha256']}), flush=True)
    return m


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--output', type=Path, default=OUT)
    ap.add_argument('--choice', type=int, default=0)
    ap.add_argument('--backend', choices=['fair-cache', 'direct'], default='direct')
    a = ap.parse_args()
    build(a.output, a.choice, a.backend)
