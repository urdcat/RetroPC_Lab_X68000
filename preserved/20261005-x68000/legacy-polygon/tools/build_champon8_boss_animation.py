"""Bounded animation update over preserved report07; no base source rewrite."""
import argparse
import json
from pathlib import Path
import re
import subprocess

import build_champon8_auto_cycle as cycle

v1 = cycle.v1
OUT = v1.DEMO / 'build/boss-animation-20260917'


def build(output=OUT, replay=1, choice=0):
    output = output.resolve()
    base = cycle.build(output / 'base07', replay, choice)
    source = (output / 'base07/guest.m68').read_text('utf8')

    def edit(name, old, new):
        nonlocal source
        body = v1.blocks(source)[name]
        assert old in body, (name, old)
        source = v1.replace_block(source, name, body.replace(old, new, 1))

    # One bounded compositor slice per renderer yield; no complete-page spin.
    edit('service', '  bsr.w composite_row\n  bra.w .stack_ok',
         '  bsr.w composite_row\n  bra.w .exit')
    edit('make_snapshots', '  move.w 78(a0),d0.w\n  move.w 80(a0),d1.w',
         '  bsr.w animation_place_boss')
    edit('cycle_capture_page', '  move.w CTX+5120+140.l,page_boss_x.l',
         '  move.w animation_page_x.l,page_boss_x.l')
    # X68000-specific 256-color, two-page GVRAM. Keep the first 16 color roles
    # and add 16 green + 16 red shades instead of emulating 1-bit sprite ink.
    edit('entry', '  clr.w $e80028.l',
         '  move.w #$0100,$e80028.l\n  move.w #1,$e82400.l')
    edit('entry', '  lea palette_words(pc),a0', '  lea animation_palette(pc),a0')
    edit('entry', '  moveq #15,d0.l', '  moveq #47,d0.l')
    edit('encode_runs', '  andi.w #15,(a0)+\n  btst #4,d0.l',
         '  andi.w #255,(a0)+\n  btst #8,d0.l')

    # Keep the original turn period, replace 8-tick stairs by a 512-step table.
    p, _ = v1.parameters()
    sine = p['sine64']
    fine = [sine[i >> 3] + ((sine[((i >> 3) + 1) & 63] - sine[i >> 3]) * (i & 7) // 8)
            for i in range(512)]
    edit('boss_snapshot', '  lsr.w #3,d1.w\n  andi.w #63,d1.w',
         '  andi.w #511,d1.w')
    edit('generate_pose', '  andi.w #63,current_angle.l',
         '  andi.w #511,current_angle.l')
    edit('cycle_pose', '  lsr.w #3,d4.w\n  andi.w #63,d4.w',
         '  andi.w #511,d4.w')
    edit('cycle_pose', '  lea sine_table(pc),a0', '  lea animation_sine512(pc),a0')
    edit('cycle_pose', '  addi.w #32,d4.w\n  andi.w #127,d4.w',
         '  addi.w #256,d4.w\n  andi.w #1023,d4.w')

    renderer = (output / 'base07/base-v1/renderer.inc').read_text('utf8')
    transform = v1.blocks(renderer)['transform_vertices']
    old = '  lea sine_table(pc),a2\n  move.w current_angle.l,d0.w'
    new = '''  cmpi.w #64,render_size.l
  beq.w .fine_angle
  lea sine_table(pc),a2
  move.w current_angle.l,d0.w'''
    assert old in transform
    transform = transform.replace(old, new, 1)
    old = '  move.w 0(a2,d0.w),d5.w\n  move.l render_model.l,a0'
    new = '''  move.w 0(a2,d0.w),d5.w
  bra.w .model
.fine_angle:
  lea animation_sine512(pc),a2
  move.w current_angle.l,d0.w
  andi.w #511,d0.w
  add.w d0.w,d0.w
  move.w 0(a2,d0.w),d4.w
  addi.w #256,d0.w
  andi.w #1023,d0.w
  move.w 0(a2,d0.w),d5.w
.model:
  move.l render_model.l,a0'''
    assert old in transform
    renderer = v1.replace_block(renderer, 'transform_vertices', transform.replace(old, new, 1))
    raster = v1.blocks(renderer)['raster_triangle']
    begin = raster.index('  move.w triangle_color.l,d2.w')
    end = raster.index('.next_row:', begin)
    raster = raster[:begin] + '''  move.w triangle_color.l,d3.w
  addi.w #272,d3.w
  cmpi.w #64,render_size.l
  beq.w .pixels
  move.w #271,d3.w
.pixels:
  move.w d3.w,(a2)+
  addq.w #1,d0.w
  cmp.w d1.w,d0.w
  blt.w .pixels
''' + raster[end:]
    renderer = v1.replace_block(renderer, 'raster_triangle', raster)
    (output / 'renderer.inc').write_text(renderer, 'utf8')
    source = source.replace((output / 'base07/base-v1/renderer.inc').as_posix(),
                            (output / 'renderer.inc').as_posix())

    edit('composite_mask', '  moveq #0,d0.l\n  move.w mask_row.l,d0.w', '''  bsr.w animation_mask_row_dirty
  tst.w d0.w
  bne.w .draw_row
  addq.l #1,animation_mask_rows_skipped.l
  bra.w .row_done
.draw_row:
  moveq #0,d0.l
  move.w mask_row.l,d0.w''')
    edit('composite_mask', '  addq.w #1,mask_row.l', '.row_done:\n  addq.w #1,mask_row.l')
    for name in ['composite_mask', 'boss_snapshot']:
        body = re.sub(r'\b(b[a-z]+)\.s ', r'\1.w ', v1.blocks(source)[name])
        source = v1.replace_block(source, name, body)
    extra = (v1.DEMO / 'boss_animation.m68').read_text('utf8')
    extra += '\n[animation_table\n' + '\n'.join(v1.array_lines('animation_sine512', fine)) + '\n]/\n'
    palette = list(base['palette'])
    for rgb in [(4, 24, 8), (30, 10, 10)]:
        for level in range(1, 17):
            r, g, b = [max(1, (c * level + 8) // 16) for c in rgb]
            palette.append((g << 11) | (r << 6) | (b << 1))
    extra += '\n[animation_palette_data\n' + '\n'.join(v1.array_lines('animation_palette', palette)) + '\n]/\n'
    source = source.replace('include "' + (output / 'renderer.inc').as_posix() + '"',
                            extra + '\ninclude "' + (output / 'renderer.inc').as_posix() + '"')
    (output / 'guest.m68').write_text(source, 'utf8')
    result = subprocess.run([str(v1.cache.ASM), 'assemble', str(output / 'guest.m68'), '-o',
                             str(output / 'guest.bin'), '--cpu', '68000', '--base-address', '0x1000'],
                            capture_output=True, text=True, encoding='utf8')
    (output / 'assembler.txt').write_text(result.stdout + result.stderr, 'utf8')
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    symbols = {k: int(v, 16) for k, v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)', result.stdout, re.M)}
    assert (output / 'guest.bin').stat().st_size + 0x1000 < 0x8000
    m = {**base, 'schema': 'x68000-boss-animation.v1', 'reportId': '20260917-x68000-boss-animation-08',
         'requestId': '20260917-boss-animation-utilization-01', 'symbols': symbols,
         'animationLogicSha256': v1.sha(v1.DEMO / 'boss_animation.m68'),
         'animationBuilderSha256': v1.sha(Path(__file__)), 'base07Sha256': base['binarySha256'],
         'binarySha256': v1.sha(output / 'guest.bin'), 'sine512': fine, 'palette': palette,
         'graphics': '512x512 256-color two-page GVRAM; 32 solid boss shades, no 1-bit face dither',
         'adaptations': ['bounded compositor yield', 'independent cached-image XY',
                         '512-step rotation and orbit at unchanged period', 'damage-row mask reuse'],
         'directGvramComparison': 'Not measured; software local-image cache is optional, not hardware sprites'}
    (output / 'build.json').write_text(json.dumps(m, indent=2), 'utf8')
    print(json.dumps({'binary': str(output / 'guest.bin'), 'bytes': (output / 'guest.bin').stat().st_size,
                      'sha256': m['binarySha256']}), flush=True)
    return m


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--output', type=Path, default=OUT)
    ap.add_argument('--choice', type=int, default=0)
    a = ap.parse_args()
    build(a.output, choice=a.choice)
