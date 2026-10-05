#!/usr/bin/env python3
"""Build the local-cache A/B guest from our native renderer and original meshes.

No commercial reference reader, ROM, geometry, pose image or host renderer input.
The existing native_fast source stays immutable; adaptations are generated with
checked replacements into this variant's ignored build directory.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / 'src/platform/x68k/champon8_poly_demo'
OUT = DEMO / 'build/local-cached-20260916'
ASM = Path('D:/work/DevelopTools/Assemblers/m68kasm/dist/m68kasm-0.9.0-preview.1-win-x64/bin/m68kasm.exe')
NATIVE_SHA = '0058571af074fc202cc27c6a21b1874877ec7f5b8e74dc7328a4fc496cc2bc2e'

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def tetra(vertices, color):
    center = [sum(v[j] for v in vertices)/4 for j in range(3)]
    faces=[]
    for a,b,c in [(0,1,2),(0,3,1),(0,2,3),(1,3,2)]:
        u=[vertices[b][j]-vertices[a][j] for j in range(3)]
        v=[vertices[c][j]-vertices[a][j] for j in range(3)]
        n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
        if sum(n[j]*(vertices[a][j]-center[j]) for j in range(3))>0:
            b,c=c,b
        faces.append([a,b,c,color])  # inward winding matches native cross > 0
    return vertices,faces

def meshes():
    # Two independently designed tetrahedral wings: 8 unique vertices / 8 faces.
    vertices=[]; faces=[]
    for sign,color in [(-1,2),(1,3)]:
        v,f=tetra([(sign*8,0,0),(sign*1,-3,5),(sign*1,3,5),(sign*2,0,-9)],color)
        faces += [[a+len(vertices),b+len(vertices),c+len(vertices),k] for a,b,c,k in f]
        vertices += v
    enemy={'vertices':vertices,'faces':faces}
    player={'vertices':[[int(x/2) for x in v] for v in vertices], 'faces':[[a,b,c,4 if k==2 else 5] for a,b,c,k in faces]}
    # Generic original six radial tetrahedra plus central octahedron: 30V/32T.
    bv=[]; bf=[]
    for axis in range(3):
        for sign in [-1,1]:
            other=[i for i in range(3) if i!=axis]
            tip=[0,0,0];tip[axis]=24*sign
            base=[]
            for x,y in [(6,0),(-3,5),(-3,-5)]:
                q=[0,0,0];q[axis]=10*sign;q[other[0]]=x;q[other[1]]=y;base.append(q)
            v,f=tetra([tip]+base,6+axis)
            bf += [[a+len(bv),b+len(bv),c+len(bv),k] for a,b,c,k in f];bv += v
    ov=[(8,0,0),(-8,0,0),(0,8,0),(0,-8,0),(0,0,8),(0,0,-8)]
    for x in [0,1]:
        for y in [2,3]:
            for z in [4,5]:
                a,b,c=x,y,z
                u=[ov[b][j]-ov[a][j] for j in range(3)];v=[ov[c][j]-ov[a][j] for j in range(3)]
                n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
                if sum(n[j]*ov[a][j] for j in range(3))>0:b,c=c,b
                bf.append([24+a,24+b,24+c,9+(x+y+z)%3])
    bv+=ov
    assert len(bv)==30 and len(bf)==32
    return {'player':player,'enemy':enemy,'boss':{'vertices':bv,'faces':bf}}

def renderer():
    p=DEMO/'native_fast.m68';assert sha(p)==NATIVE_SHA
    s=p.read_text('utf8')
    code=s[s.index('[transform_vertices'):s.index('[commit_bounds')]+s[s.index('[build_face_order'):s.index('[demo_data')]
    data=s[s.index('[demo_data'):].replace('include "build/model_generated.inc"','')
    def change(old,new):
        nonlocal code
        assert old in code,old
        code=code.replace(old,new)
    change('lea model_vertices(pc),a0','move.l render_model.l,a0')
    change('lea model_faces(pc),a0','move.l render_faces.l,a0')
    change('lea model_faces(pc),a2','move.l render_faces.l,a2')
    change('move.w #MODEL_VERTEX_COUNT,vertices_left.l','move.w render_vertex_count.l,vertices_left.l')
    change('cmpi.w #MODEL_FACE_COUNT,d7.w','cmp.w render_face_count.l,d7.w')
    change('addi.w #CAMERA_DEPTH,d0.w','move.w d0.w,pose_rotated_z.l\n  add.w pose_depth.l,d0.w\n  cmpi.w #64,d0.w\n  bge.w .safe_depth\n  move.w #1,pose_invalid.l\n  move.w #64,d0.w\n.safe_depth:')
    for axis in ['X','Y']:
        low=axis.lower()
        change(f'muls.w #PROJECTION_FOCAL_{axis},d1.l',f'muls.w #512,d1.l\n  move.w pose_view_{low}.l,d2.w\n  muls.w pose_rotated_z.l,d2.l\n  sub.l d2.l,d1.l')
        change(f'addi.w #SCREEN_CENTER_{axis},d1.w','add.w local_center.l,d1.w')
    for reg in ['d0','d1']:
        for limit in ['SCREEN_WIDTH','SCREEN_HEIGHT']:
            old=f'cmpi.w #{limit},{reg}.w'
            if old in code:change(old,f'cmp.w render_size.l,{reg}.w')
    change('move.w #SCREEN_WIDTH,d1.w','move.w render_size.l,d1.w')
    change('move.w #SCREEN_HEIGHT,d1.w','move.w render_size.l,d1.w')
    change('lsl.l #8,d2.l\n  lsl.l #2,d2.l','mulu.w render_stride.l,d2.l')
    change('adda.l #1024,a4','adda.w render_stride.l,a4')
    change('move.w (a2),triangle_color.l','move.w (a2),triangle_color.l\n  ori.w #16,triangle_color.l')
    # Cooperative service preserves all registers and CCR (native branch flags).
    for label in ['.vertex:','.face:','.shift:','.scanline:']:
        change(label,label+'\n  bsr.w service')
    # No entire-frame regeneration here. Each invocation draws only one local pose.
    return code+data

def asset_source(models):
    lines=['[local_assets']
    for name,m in models.items():
        lines += [name+'_vertices:','  defw '+','.join(str(n) for v in m['vertices'] for n in v),name+'_faces:','  defw '+','.join(str(n) for f in m['faces'] for n in f)]
    sine=[round(math.sin(2*math.pi*i/256)*16384) for i in range(256)]
    lines += ['sine_table:','  defw '+','.join(map(str,sine))]
    rgb=[(0,0,0),(2,4,8),(0,12,22),(0,22,31),(18,22,31),(26,31,31),(0,12,2),(0,22,4),(10,31,8),(16,0,1),(24,2,2),(31,6,3),(8,8,8),(31,6,23),(31,26,4),(31,31,31)]
    palette=[(g<<11)|(r<<6)|(b<<1) for r,g,b in rgb]
    lines += ['palette_words:','  defw '+','.join(map(str,palette)), 'context_templates:']
    for i in range(6):
        name='player' if i==0 else 'boss' if i==5 else 'enemy';w=64 if i==5 else 16
        base=0x24000 if i==5 else 0x20000+i*2688;sz=20736 if i==5 else 1344
        lines += [f'  defl {name}_vertices,{name}_faces',f'  defw {len(models[name]["vertices"])},{len(models[name]["faces"])},{w},{w*2}',f'  defl {base},{base+sz}']
    stars=[];state=0x1B02
    for i in range(160):
        state=(1664525*state+1013904223)&0xffffffff;x=(state>>10)&511
        state=(1664525*state+1013904223)&0xffffffff;y=(state>>10)&511
        stars.append([x,y,12 if i%11==0 else 1])
    lines += ['starfield:','  defw '+','.join(str(n) for p in stars for n in p), 'projected_vertices:','  defw '+','.join(['0']*96),'face_order:','  defl '+','.join(['0']*64), 'dirty_span_left:','  defw '+','.join(['0']*64),'dirty_span_right:','  defw '+','.join(['0']*64)]
    return '\n'.join(lines)+'\n]/\n',sine,palette,stars

def build(output=OUT,replay=False,scene=0):
    output.mkdir(parents=True,exist_ok=True)
    models=meshes();assets,sine,palette,stars=asset_source(models)
    (output/'renderer.inc').write_text(renderer(),'utf8');(output/'assets.inc').write_text(assets,'utf8')
    source=(DEMO/'local_cached.m68').read_text('utf8').replace('include "LOCAL_RENDERER"','include "'+(output/'renderer.inc').as_posix()+'"').replace('include "LOCAL_ASSETS"','include "'+(output/'assets.inc').as_posix()+'"').replace('REPLAY_DEFAULT equ 0',f'REPLAY_DEFAULT equ {int(replay)}')
    source=source.replace('SCENE_DEFAULT equ 0',f'SCENE_DEFAULT equ {scene}')
    (output/'guest.m68').write_text(source,'utf8')
    result=subprocess.run([str(ASM),'assemble',str(output/'guest.m68'),'-o',str(output/'guest.bin'),'--cpu','68000','--base-address','0x1000'],capture_output=True,text=True,encoding='utf8')
    (output/'assembler.txt').write_text(result.stdout+result.stderr,'utf8')
    if result.returncode:raise RuntimeError(result.stdout+result.stderr)
    symbols={k:int(v,16) for k,v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)',result.stdout,re.M)}
    assert (output/'guest.bin').stat().st_size+0x1000<0x10000
    manifest={'schema':'x68000-local-cache.v1','productionRequestId':'20260916-local-rendering-production-start-01','models':models,'sine':sine,'palette':palette,'stars':stars,'symbols':symbols,'sourceSha256':sha(DEMO/'local_cached.m68'),'nativeSourceSha256':NATIVE_SHA,'builderSha256':sha(Path(__file__)),'binarySha256':sha(output/'guest.bin'),'assemblerSha256':sha(ASM),'replayDefault':replay,'assetOrigin':'independently authored mathematical tetrahedra/octrahedron; no reference geometry or ROM','hardwareVerified':False}
    manifest['sceneDefault']=scene
    (output/'build.json').write_text(json.dumps(manifest,indent=2),'utf8')
    print(json.dumps({'binary':str(output/'guest.bin'),'bytes':(output/'guest.bin').stat().st_size,'sha256':manifest['binarySha256']}),flush=True)
    return manifest

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=OUT);p.add_argument('--replay',action='store_true');p.add_argument('--scene',type=int,choices=[0,1],default=0);a=p.parse_args();build(a.output,a.replay,a.scene)
