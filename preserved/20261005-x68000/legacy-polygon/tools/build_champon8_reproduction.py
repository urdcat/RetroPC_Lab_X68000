#!/usr/bin/env python3
"""Build the SPEC v1 consumer without changing the hash-bound report04 guest."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

import build_champon8_local_cached as cache

ROOT=Path(__file__).resolve().parents[1]
DEMO=cache.DEMO
PACKAGE=Path('D:/work/PolygonGames/specs/20260916-pyuta-cross-machine-reproduction-v1')
OUT=DEMO/'build/reproduction-20260917'
BASE_SHA='015321a326eda4fac2d08d0aee15a5792e8145cd507df7c16db72385733b2c50'

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def blocks(s):return {m[1]:m[0] for m in re.finditer(r'^\[([a-zA-Z_][a-zA-Z_0-9]*)\n.*?^\](?:/)?',s,re.M|re.S)}
def replace_block(s,name,new):
    old=blocks(s)[name];return s.replace(old,new,1)
def array_lines(label,values,kind='defw'):
    values=list(values);return [label+':']+['  '+kind+' '+','.join(map(str,values[i:i+24])) for i in range(0,len(values),24)]

def parameters():
    manifest=json.loads((PACKAGE/'manifest.json').read_text('utf8'))
    for f in manifest['files']:assert sha(PACKAGE/f['path'])==f['sha256'],f['path']
    return json.loads((PACKAGE/'parameters.json').read_text('utf8')),manifest

def image_blob(size,color,w,h):
    import struct
    n=size*size;blob=bytearray(n*5+size*4);cursor=n*4+size*4
    for y in range(size):
        lit=size//2-h//2<=y<size//2-h//2+h
        struct.pack_into('>HH',blob,n*4+y*4,cursor,int(lit))
        if lit:
            x=size//2-w//2;struct.pack_into('BB',blob,cursor,x,w);cursor+=2
            for xx in range(x,x+w):
                struct.pack_into('>H',blob,(y*size+xx)*2,color)
                struct.pack_into('>H',blob,n*2+(y*size+xx)*2,65535)
    return bytes(blob)

def assets(p):
    original=cache.meshes()['enemy']
    small={'vertices':[[v*4 for v in point] for point in original['vertices']], 'faces':[[a,b,c,[15,12,8,10][i%4]] for i,(a,b,c,_) in enumerate(original['faces'])]}
    models={'player':small,'enemy':small,'boss':p['boss']['geometry']}
    lines=['[repro_assets']
    for name,m in models.items():
        lines+=array_lines(name+'_vertices',(n for point in m['vertices'] for n in point))
        lines+=array_lines(name+'_faces',(n for face in m['faces'] for n in face))
    lines+=array_lines('sine_table',p['sine64'])
    lines+=array_lines('dither_table',(v for row in p['dither16x8'] for v in row),'defb')
    lines+=array_lines('bayer_table',(v for row in p['bayer4x4'] for v in row),'defb')
    lines+=array_lines('mask_patterns',(sum(1<<x for x in range(4) if p['bayer4x4'][y][x]>=level) for level in range(17) for y in range(4)),'defb')
    lines+=['mask_writers:','  defl '+','.join('mask_case_'+str(i) for i in range(16))]
    lines+=array_lines('mask_write_bytes',(i.bit_count()*128 for i in range(16)))
    for pattern in range(16):
        lines+=['mask_case_'+str(pattern)+':']
        # One four-source-pixel group, expanded 2x2. Caller repeats 16 times.
        lines += [f'  move.w d2.w,{x*2+dy}(a0)' for x in range(8) if pattern&(1<<((x>>1)&3)) for dy in [0,1024]]
        lines+=['  rts']
    lines+=array_lines('wave_delays',(v for row in p['waves']['delays'] for v in row))
    lines+=array_lines('loop_table',(v for xyz in p['waves']['loop512'] for v in xyz))
    lines+=array_lines('rain_table',p['waves']['rainX16'])
    # Same color roles/indices, explicit 5-bit RGB approximation on X68000.
    rgb=[(0,0,0),(0,0,0),(4,24,8),(11,27,14),(10,10,27),(14,15,30),(26,9,9),(8,26,28),(30,10,10),(31,16,16),(26,24,10),(28,27,15),(4,17,7),(25,11,23),(24,24,24),(31,31,31)]
    palette=[(g<<11)|(r<<6)|(b<<1) for r,g,b in rgb]
    lines+=array_lines('palette_words',palette)
    lines+=['context_templates:']
    for i in range(6):
        name='player' if i==0 else 'boss' if i==5 else 'enemy';w=64 if i==5 else 16
        base=0x24000 if i==5 else 0x20000+i*2688;n=20736 if i==5 else 1344
        lines+=[f'  defl {name}_vertices,{name}_faces',f'  defw {len(models[name]["vertices"])},{len(models[name]["faces"])},{w},{w*2}',f'  defl {base},{base+n}']
    stars=[[s['screen'][0]*2,s['screen'][1]*2+64,s['paletteIndex']] for s in p['stars']['points']]
    lines+=array_lines('starfield',(v for star in stars for v in star))
    lookup={s['tileIndex']:s for s in p['stars']['points']}
    fade=[0x40000+(lookup[t]['screen'][1]*2+64)*1024+lookup[t]['screen'][0]*4 for t in p['stars']['fadeTileOrder']]
    lines+=array_lines('fade_addresses',fade,'defl')
    lines+=array_lines('job_order',[1,2,3,4,0,5])
    lines+=['shot_image_table:','  defl shot_p0,shot_p1,shot_p2,shot_e0,shot_e1,shot_e2']
    for team,color in [('p',13),('e',11)]:
        for i,(w,h) in enumerate(p['shots']['sizes']):lines+=array_lines(f'shot_{team}{i}',image_blob(16,color,w,h),'defb')
    for name,count,kind in [('projected_vertices',96,'defw'),('face_order',64,'defl'),('dirty_span_left',64,'defw'),('dirty_span_right',64,'defw')]:lines+=array_lines(name,[0]*count,kind)
    lines+=array_lines('key_commands',[65,0,68,1,87,2,83,3,97,0,100,1,119,2,115,3],'defb')
    lines+=[']/']
    return '\n'.join(lines)+'\n',models,palette,stars

def build(output=OUT,replay=1,choice=0):
    p,manifest=parameters();base=DEMO/'local_cached.m68';assert sha(base)==BASE_SHA
    source=base.read_text('utf8');logic=(DEMO/'reproduction_logic.m68').read_text('utf8');extra=[]
    overrides=blocks(logic)
    for name,body in overrides.items():
        if name in blocks(source):source=replace_block(source,name,body)
        elif name not in ['transform_vertices','raster_triangle']:extra.append(body)
    constants=logic[:logic.index('[')]
    source=constants+source.replace('REPLAY_DEFAULT equ 0',f'REPLAY_DEFAULT equ {replay}')
    source=source.replace('CHOICE_DEFAULT equ 0',f'CHOICE_DEFAULT equ {choice}')
    # Reuse only the initialization prefix; game worker is replaced below.
    entry=blocks(source)['entry'];entry=entry[:entry.index('.worker:')].replace('  move.w #$a55a,S_READY.l\n','')+'''  bsr.w reset_game
  move.w #CHOICE_DEFAULT,S_CHOICE.l
  move.w #$a55a,S_READY.l
  bra.w game_loop
]/'''
    entry=entry.replace('move.w #159,d7.w','move.w #59,d7.w').replace('  mulu.w #37,d0.l','  clr.w d0.w').replace('  bsr.w update_world','  nop')
    entry=entry.replace('  move.w (a0)+,(a1)\n','  move.w (a0)+,d2.w\n  move.w d2.w,(a1)\n  move.w d2.w,2(a1)\n  move.w d2.w,1024(a1)\n  move.w d2.w,1026(a1)\n')
    source=replace_block(source,'entry',entry)
    # Retain the hash-bound damage/run engine, replace only its snapshot input.
    comp=blocks(source)['start_composite']
    comp='[start_composite\n  move.l S_TICK.l,S_SNAPTICK.l\n  bsr.w make_snapshots\n'+comp[comp.index('  // Sort snapshot'):]
    comp=comp.replace('.page:\n','.page:\n  bsr.w refresh_background\n')
    comp=comp.replace('  move.l (a3),d0.l\n  cmp.l (a4),d0.l','  tst.w force_damage.l\n  bne.s .changed\n  move.l (a3),d0.l\n  cmp.l (a4),d0.l')
    comp=comp.replace('  clr.w composite_y.l','  bsr.w prepare_mask\n  clr.w composite_y.l',1)
    source=replace_block(source,'start_composite',comp)
    comp=blocks(source)['composite_row'].replace('.finished:\n  clr.w composing.l','.finished:\n  bsr.w composite_mask\n  tst.w d0.w\n  beq.s .done\n  clr.w composing.l')
    comp=comp.replace('  move.w 6(a0),d0.w\n  sub.w 2(a4),d0.w','  tst.w composite_y.l\n  bne.s .damage_checked\n  bsr.w object_intersects_damage\n  tst.w d0.w\n  beq.w .next_object\n.damage_checked:\n  move.w 6(a0),d0.w\n  sub.w 2(a4),d0.w')
    # A bounded intersection test extends the distance to these branches.
    comp=comp.replace('bge.s .finished','bge.w .finished').replace('beq.s .next_object','beq.w .next_object')
    source=replace_block(source,'composite_row',comp)
    rect=blocks(source)['add_rectangle'].replace('  tst.w d1.w\n  bpl.s .yok\n  clr.w d1.w','  cmpi.w #64,d1.w\n  bge.s .yok\n  move.w #64,d1.w').replace('#512,d4.w','#448,d4.w')
    source=replace_block(source,'add_rectangle',rect)
    blit=blocks(source)['blit_object_row']
    blit=blit.replace('[blit_object_row\n','[blit_object_row\n  btst #0,composite_y+1.l\n  bne.w .done\n')
    blit=blit.replace('  bmi.w .done\n  cmpi.w #512,d0.w','  cmpi.w #64,d0.w\n  blt.w .done\n  cmpi.w #448,d0.w')
    blit=blit.replace('  move.w 4(a4),d3.w\n  mulu.w d3.w,d3.l','  move.w 4(a4),d3.w\n  lsr.w #1,d3.w\n  mulu.w d3.w,d3.l')
    blit=blit.replace('  move.w composite_y.l,d4.w\n  lsl.w #2,d4.w','  move.w composite_y.l,d4.w\n  lsr.w #1,d4.w\n  lsl.w #2,d4.w')
    blit=blit.replace('  move.w composite_y.l,d4.w\n  mulu.w 4(a4),d4.l','  move.w composite_y.l,d4.w\n  lsr.w #1,d4.w\n  move.w 4(a4),d3.w\n  lsr.w #1,d3.w\n  mulu.w d3.w,d4.l')
    blit=blit.replace('  move.b (a2)+,d1.b\n  move.l a2,run_cursor.l','  move.b (a2)+,d1.b\n  add.w d0.w,d0.w\n  add.w d1.w,d1.w\n  move.l a2,run_cursor.l')
    blit=blit.replace('  sub.w (a4),d1.w\n  add.w d1.w,d1.w','  sub.w (a4),d1.w')
    blit=blit.replace('  bsr.w copy_words','  bsr.w copy_scaled_words')
    source=replace_block(source,'blit_object_row',blit)
    # Original local data already defines key_commands, assets own the new copy.
    source=source.replace('key_commands:\n  defb 65,0,68,1,87,2,83,3,97,0,100,1,119,2,115,3','')
    source=source.replace('include "LOCAL_RENDERER"','\n'.join(extra)+'\ninclude "LOCAL_RENDERER"')
    renderer=cache.renderer()
    for name in ['transform_vertices','raster_triangle']:renderer=replace_block(renderer,name,overrides[name])
    renderer=renderer.replace('  ori.w #16,triangle_color.l','')
    # Legacy unused span raster is retained only as immutable reusable source.
    # Completion snapshots use fixed sprite priority, not world-depth sorting.
    output.mkdir(parents=True,exist_ok=True);assets_text,models,palette,stars=assets(p)
    (output/'renderer.inc').write_text(renderer,'utf8');(output/'assets.inc').write_text(assets_text,'utf8')
    source=source.replace('include "LOCAL_RENDERER"','include "'+(output/'renderer.inc').as_posix()+'"').replace('include "LOCAL_ASSETS"','include "'+(output/'assets.inc').as_posix()+'"')
    (output/'guest.m68').write_text(source,'utf8')
    result=subprocess.run([str(cache.ASM),'assemble',str(output/'guest.m68'),'-o',str(output/'guest.bin'),'--cpu','68000','--base-address','0x1000'],capture_output=True,text=True,encoding='utf8')
    (output/'assembler.txt').write_text(result.stdout+result.stderr,'utf8')
    if result.returncode:raise RuntimeError(result.stdout+result.stderr)
    symbols={k:int(v,16) for k,v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)',result.stdout,re.M)}
    assert (output/'guest.bin').stat().st_size+0x1000<0x10000
    m={'schema':'x68000-pyuta-reproduction.v1','specId':p['specId'],'packageManifest':manifest,'models':models,'palette':palette,'stars':stars,'symbols':symbols,'baseSha256':BASE_SHA,'logicSha256':sha(DEMO/'reproduction_logic.m68'),'builderSha256':sha(Path(__file__)),'binarySha256':sha(output/'guest.bin'),'assemblerSha256':sha(cache.ASM),'replay':replay,'choice':choice,'viewport':{'logical':[256,192],'physical':[512,512],'origin':[0,64],'scale':[2,2],'occupied':[512,384]},'physicalInputVerified':False,'hardwareVerified':False}
    (output/'build.json').write_text(json.dumps(m,indent=2),'utf8');print(json.dumps({'binary':str(output/'guest.bin'),'bytes':(output/'guest.bin').stat().st_size,'sha256':m['binarySha256']}),flush=True)
    return m

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=OUT);p.add_argument('--replay',type=int,default=1);p.add_argument('--choice',type=int,choices=[0,1],default=0);a=p.parse_args();build(a.output,a.replay,a.choice)
