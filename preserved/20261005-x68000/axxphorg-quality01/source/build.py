"""Build a separate MC68000 RAM-entry AxxPhorg consumer; never edit old demos.

Only backgrounds and scalar material lighting are computed offline. The guest
projects, culls, sorts and rasterizes player, enemies and destructible parts.
"""
from pathlib import Path
import argparse, hashlib, importlib.util, json, math, re, struct, subprocess, sys
import numpy as np
sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
SOURCE = Path(json.loads((HERE/'settings.json').read_text('utf8'))['sourceDirectory'])
OUT = HERE / 'build'
sys.path.insert(0, str(REPO / 'tools'))
import build_champon8_local_cached as cache

def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def save(p, value): p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', 'utf8')
def array(name, values, kind='defw'):
    values=list(values)
    return name+':\n'+''.join('  '+kind+' '+','.join(map(str,values[i:i+24]))+'\n' for i in range(0,len(values),24))

def enrich(base, kind):
    m={'vertices':[list(v) for v in base['vertices']], 'faces':[list(f) for f in base['faces']]}
    # Inherited paired tetrahedral wings, plus original fuselage/canopy/engines.
    def octa(center, radii, color):
        off=len(m['vertices']); cx,cy,cz=center; x,y,z=radii
        v=[(cx+x,cy,cz),(cx-x,cy,cz),(cx,cy+y,cz),(cx,cy-y,cz),(cx,cy,cz+z),(cx,cy,cz-z)]
        for a,b,c in [(a,b,c) for a in (0,1) for b in (2,3) for c in (4,5)]:
            u=np.asarray(v[b])-v[a]; w=np.asarray(v[c])-v[a]
            if np.dot(np.cross(u,w),np.asarray(v[a])-center)>0: b,c=c,b
            m['faces'].append([a+off,b+off,c+off,color])
        m['vertices']+=v
    if kind=='part':
        m={'vertices':[],'faces':[]}
        octa((0,0,0),(12,10,16),0); octa((0,-7,0),(7,6,9),1)
        octa((0,5,-10),(6,4,7),2)
    else:
        m['vertices']=[[n*2 for n in v] for v in m['vertices']]
        m['faces']=[[a,b,c,0 if k%2 else 1] for a,b,c,k in m['faces']]
        octa((0,0,0),(5,4,18),0); octa((0,-4,3),(4,3,7),1)
        octa((0,-1,-13),(5,3,5),2)
    assert len(m['vertices'])<=32 and len(m['faces'])<=64
    return m

def backgrounds(settings):
    # Independent RLE tile maps allow arbitrary pose skipping without replaying
    # every intermediate delta. Dictionary tiles are immutable packed 4bpp.
    seq=json.loads((SOURCE/'sequence.json').read_text('utf8'))
    track=json.loads((SOURCE/'object-motion-track.json').read_text('utf8'))['frames']
    tracks={(f['part'],f['frame']):f for f in track}
    data=np.load(SOURCE/'media/indexed-source-frames.npz')
    frames=[]; maps=[]; stats=[]; dictionary={bytes(32):0}
    stride=settings['backgroundSampleStride']
    for pi,part in enumerate(seq['parts']):
        a=data[part['id']];first=len(frames)
        for index in range(0,len(a),stride):
            blocks=a[index].reshape(24,8,32,8).transpose(0,2,1,3).reshape(768,64)
            tile_ids=[]
            for block in blocks:
                assert int(block.max())<16
                packed=bytes((block[::2]<<4)|block[1::2])
                tile_ids.append(dictionary.setdefault(packed,len(dictionary)))
            tile_ids=np.asarray(tile_ids,dtype=np.uint16)
            edges=np.r_[0,np.flatnonzero(tile_ids[1:]!=tile_ids[:-1])+1,768]
            encoded=b''.join(struct.pack('>HH',int(r-l),int(tile_ids[l])) for l,r in zip(edges[:-1],edges[1:]))
            maps.append(encoded);count=len(encoded)//4;t=tracks[(part['id'],index)]
            assert t['principalPointPixels']==[128,96], 'normal-play projection must be fixed'
            obj=next((o for o in t['objects'] if o['objectId']==20),None)
            center=obj['centerWorld'] if obj else [70,0,30]
            frames.append([0,count,index,round(center[0]),round(center[2]),round(t['principalPointPixels'][1]*2+64),round(index/96*256/6)])
        stats.append({'id':part['id'],'first':first,'count':len(frames)-first,'sourceFrames':len(a),'durationSeconds':part['durationSeconds']})
    assert len(dictionary)<16384
    payload=bytearray().join(p for p,_ in sorted(dictionary.items(),key=lambda q:q[1]))
    for frame,encoded in zip(frames,maps):
        if len(payload)<0x60000 and len(payload)+len(encoded)>0x60000:
            payload+=bytes(0x60000-len(payload))
        offset=len(payload)
        frame[0]=0x10000+offset if offset<0x60000 else 0xb6000+offset-0x60000
        payload+=encoded
    if len(payload)>0x60000+0x37800: raise ValueError('tile dictionary/maps exceed standard 1MiB layout')
    settings['_codec']={'name':'independent-8x8-tile-dictionary-map-v1',
        'dictionaryTiles':len(dictionary),'dictionaryBytes':len(dictionary)*32,
        'mapBytes':sum(map(len,maps)),'physicalTilePixels':[16,16],
        'currentTileIdsAddress':0xed800,'unchangedTileDecodeAndTransferSkipped':True}
    (OUT/'background.bin').write_bytes(payload)
    return frames,stats,seq

def build():
    OUT.mkdir(parents=True,exist_ok=True)
    cfg=json.loads((HERE/'settings.json').read_text('utf8'))
    assert sha(SOURCE/'manifest.json')==cfg['sourceManifestSha256']
    manifest=json.loads((SOURCE/'manifest.json').read_text('utf8'))
    for entry in manifest['files']:
        assert sha(SOURCE/entry['path'])==entry['sha256'], entry['path']
    frames,parts,seq=backgrounds(cfg)
    models={n:enrich(cache.meshes()['player' if n=='player' else 'enemy'],n) for n in ('player','enemy','part')}
    source=(HERE/'guest.m68').read_text('utf8')
    quality=(HERE/'quality.m68').read_text('utf8')
    for match in re.finditer(r'(?ms)^\[(\w+)\n.*?^\](?:/)?',quality):
        name=match[1]; block=match[0]
        pattern=r'(?ms)^\['+re.escape(name)+r'\n.*?^\](?:/)?'
        if re.search(pattern,source):source=re.sub(pattern,lambda _:block,source,count=1)
        else:source=source.replace('include "RENDERER"',block+'\ninclude "RENDERER"')
    renderer=cache.renderer().replace('ori.w #16,triangle_color.l','ori.w #256,triangle_color.l')
    renderer=renderer.replace('muls.w #512,d1.l','muls.w #310,d1.l')
    pitch='''  move.w current_angle.l,d0.w
  lsr.w #1,d0.w
  addi.w #24,d0.w
  andi.w #$00ff,d0.w
  add.w d0.w,d0.w
  move.w 0(a2,d0.w),d6.w       // pitch sine
  addi.w #128,d0.w
  andi.w #$01ff,d0.w
  move.w 0(a2,d0.w),d7.w       // pitch cosine'''
    assert pitch in renderer
    renderer=renderer.replace(pitch,'  move.w #10531,d6.w\n  move.w #12551,d7.w')
    # Native camera-space down axis: mesh local +Y points down, world +Y up.
    assets='[rich_assets\n'
    for name,m in models.items():
        assets+=array(name+'_vertices',(v for p in m['vertices'] for v in p))
        assets+=array(name+'_faces',(v for a,b,c,k in m['faces'] for v in [a,b,c,64+k*8+4]))
        materials=[]
        normals=[]
        for a,b,c,k in m['faces']:
            n=np.cross(np.array(m['vertices'][b])-m['vertices'][a],np.array(m['vertices'][c])-m['vertices'][a]).astype(float)
            n/=-max(np.linalg.norm(n),1); normals.append((n,k))
        for angle in range(256):
            s,c=math.sin(angle*math.tau/256),math.cos(angle*math.tau/256)
            for n,k in normals:
                x=n[0]*c+n[2]*s; z=n[2]*c-n[0]*s
                light=max(0.0, x*-.35+n[1]*-.7+z*-.62)
                materials.append(64+k*8+min(7,int(1.5+light*6)))
        assets+=array(name+'_shades',materials,'defb')+'  align 2\n'
    sine=[round(math.sin(i*math.tau/256)*16384) for i in range(256)]
    assets+=array('sine_table',sine)
    palette=json.loads((SOURCE/'palette.json').read_text('utf8'))['colors']
    palette += [(0,0,0)]*(64-len(palette))
    for rgb in [(105,170,200),(240,152,72),(62,232,222)]:
        palette += [tuple(round(c*(.28+.72*i/7)) for c in rgb) for i in range(8)]
    palette += [(255,255,210),(255,144,45),(48,208,255),(255,240,130),(255,255,255)]
    palette += [(0,0,0)]*(256-len(palette))
    words=[((g*31//255)<<11)|((r*31//255)<<6)|((b*31//255)<<1) for r,g,b in palette]
    assets+=array('rich_palette',words)
    # Indexed pair -> two horizontally duplicated word pixels. Reused for both
    # vertical rows; generated bytes are scalar conversion, not image poses.
    assets+=array('background_pair_lut',(v for b in range(256) for v in [(b>>4)*65537,(b&15)*65537]),'defl')
    local_images=[];address=0x70000
    assets+='image_descriptors:\n'
    for slot in range(7):
        size=192 if slot==0 else 128
        assets+=f'  defl {address}\n  defw {size},{size*2},{size//2},0\n'
        local_images.append({'slot':slot,'address':address,'size':size,'stride':size*2,'boundsAddress':0xb4000+slot*768})
        address+=size*size*2
    assert address==0xb2000
    assets+=array('frame_table',(v for f in frames for v in [])) if False else 'frame_table:\n'
    for a,n,index,x,z,principal,angle in frames:
        assets+=f'  defl {a}\n  defw {n},{index},{x},{z},{principal},{angle}\n'
    assets+=array('part_table',(v for p in parts for v in [p['first'],p['count'],p['sourceFrames']]))
    assets+=array('playlist0',[seq['parts'].index(next(p for p in seq['parts'] if p['id']==s)) for s in cfg['playlists'][0]]+[-1])
    assets+=array('playlist1',[seq['parts'].index(next(p for p in seq['parts'] if p['id']==s)) for s in cfg['playlists'][1]]+[-1])
    assets+=array('projected_vertices',[0]*96)+array('face_order',[0]*64,'defl')+array('dirty_span_left',[0]*64)+array('dirty_span_right',[0]*64)
    assets+='model_descriptors:\n'
    for name,m in models.items():
        assets+=f'  defl {name}_vertices,{name}_faces,{name}_shades\n  defw {len(m["vertices"])},{len(m["faces"])}\n'
    assets+=']/\n'
    pieces=re.findall(r'(?ms)^([a-zA-Z0-9_]+):\n(.*?)(?=^[a-zA-Z0-9_]+:\n|^\]/)',assets)
    late={n+'_'+k for n in models for k in ['vertices','faces','shades']}
    assets='[rich_assets\n'+''.join(n+':\n'+body for n,body in pieces if n not in late)+''.join(n+':\n'+body for n,body in pieces if n in late)+']/\n'
    source=source.replace('include "RENDERER"',renderer).replace('include "ASSETS"',assets)
    source=source.replace('SAMPLE_STRIDE equ 4',f'SAMPLE_STRIDE equ {cfg["backgroundSampleStride"]}')
    source=source.replace('BOSS_HOLD_LOOPS equ 6',f'BOSS_HOLD_LOOPS equ {cfg["bossHoldLoops"]}')
    for symbol,key,default in [('NORMAL_LIMIT','normalEnemyLimit',4),('SHIP_LIMIT','battleshipEnemyLimit',1),('NORMAL_INTERVAL','normalSpawnIntervalTicks',80),('SHIP_INTERVAL','battleshipSpawnIntervalTicks',240),('PLAYER_STEP','playerWorldUnitsPerTick',2),('BOSS_HP','bossPartHp',1)]:
        source=source.replace(f'{symbol} equ {default}',f'{symbol} equ {cfg[key]}')
    (OUT/'guest.m68').write_text(source,'utf8')
    p=subprocess.run([str(cache.ASM),'assemble',str(OUT/'guest.m68'),'-o',str(OUT/'guest.bin'),'--cpu','68000','--base-address','0x1000'],capture_output=True,text=True,encoding='utf8')
    (OUT/'assembler.txt').write_text(p.stdout+p.stderr,'utf8')
    if p.returncode: raise RuntimeError((p.stdout+p.stderr)[-7000:])
    symbols={k:int(v,16) for k,v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)',p.stdout,re.M)}
    assert (OUT/'guest.bin').stat().st_size+0x1000<=0x10000
    result={'schema':'axxphorg-x68000-rich.v1','sourceManifestSha256':cfg['sourceManifestSha256'],'backgroundBytes':(OUT/'background.bin').stat().st_size,'guestBytes':(OUT/'guest.bin').stat().st_size,'binarySha256':sha(OUT/'guest.bin'),'backgroundSha256':sha(OUT/'background.bin'),'sourceSha256':sha(HERE/'guest.m68'),'builderSha256':sha(__file__),'inheritedRendererSha256':cache.NATIVE_SHA,'assemblerSha256':sha(cache.ASM),'symbols':symbols,'models':models,'palette':palette,'parts':parts,'settings':cfg,'memory':{'code':[0x1000,0x10000],'backgroundStreams':[[0x10000,0x70000],[0xe0000,0xed800]],'sevenLocalImages':[0x70000,0x7e000],'backgroundShadow':[0x80000,0xe0000],'currentTileIds':[0xed800,0xede00],'scratch':[0xee000,0xf0000],'statusAndInput':[0xf0000,0xf1000],'stackTop':0xff000,'ramBytes':1048576,'physicalGvramBytes':524288},'background':{'source':[256,192],'physical':[512,384],'origin':[0,64],'samplesPerSourceSecond':12/cfg['backgroundSampleStride'],'ssaa':False,'codec':cfg['_codec']},'runtime3d':True,'prerenderedForegroundPoses':False,'lighting':'scalar face-light LUT, not pose images','physicalInputVerified':False,'hardwareVerified':False}
    result.update(qualityVersion='20261005-quality-repair-01',localImages=local_images,
        qualitySourceSha256=sha(HERE/'quality.m68'),sourceDirectory=str(SOURCE),
        projection={'cameraPosition':[0,120,-140],'pitchDegrees':40,'focalPhysicalPixels':310,
                    'principalPhysicalPixels':[256,256],'fixedDuringNormalPlay':True},
        compositor='per-page changed-tile restoration; clipped dirty-tile foreground spans; all changed local poses completed',
        sourceFiles={p:sha(HERE/p) for p in ['build.py','guest.m68','quality.m68','settings.json']})
    result['memory'].pop('sevenLocalImages');result['memory'].pop('backgroundShadow')
    result['memory'].update(localImages=[0x70000,0xb2000],pageObjectSnapshots=[0xb3000,0xb3150],
        backgroundStreams=[[0x10000,0x70000],[0xb6000,0xed800]],
        drawDirtyTiles=[0xb3200,0xb3500],rowBounds=[0xb4000,0xb5500],pageDirtyTiles=[0xee000,0xee600])
    save(OUT/'build.json',result)
    print(json.dumps({k:result[k] for k in ('backgroundBytes','guestBytes','binarySha256')},ensure_ascii=False),flush=True)
    return result

if __name__=='__main__': build()
