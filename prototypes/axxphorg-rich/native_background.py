"""Native512 background adaptation of the immutable common procedural art.

This is offline BACKGROUND rasterization, not guest 3D/performance evidence.
World camera, materials, models, paths and the 12Hz source clock are unchanged.
Repeat-row/color-run tiles replace 4bpp upscaling without increasing guest RAM.
"""
import importlib.util,json,struct
import numpy as np
from probe_native_background import row_runs

def build_backgrounds(settings,source,out):
    spec=importlib.util.spec_from_file_location('frozen_background_renderer',source/'render_sequence.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.W,module.H,module.FOCAL=512,384,310.0
    module.principal_point=lambda part,t:(256,192)
    models=module.make_models()
    seq=json.loads((source/'sequence.json').read_text('utf8'))
    track=json.loads((source/'object-motion-track.json').read_text('utf8'))['frames']
    tracks={(f['part'],f['frame']):f for f in track}
    frames=[];maps=[];stats=[];dictionary={};oracles={}
    for pi,part in enumerate(seq['parts']):
        first=len(frames);images=[]
        for index in range(0,part['frameCount'],settings['backgroundSampleStride']):
            image,_,_=module.render(pi,index/12,models);images.append(image)
            blocks=image.reshape(24,16,32,16).transpose(0,2,1,3).reshape(768,16,16)
            ids=np.asarray([dictionary.setdefault(row_runs(block),len(dictionary)) for block in blocks],dtype=np.uint16)
            edges=np.r_[0,np.flatnonzero(ids[1:]!=ids[:-1])+1,768]
            tokens=[struct.pack('>H',int(ids[l])) if r-l==1 else
                struct.pack('>HH',int(r-l)|0x8000,int(ids[l])) for l,r in zip(edges[:-1],edges[1:])]
            encoded=b''.join(tokens)
            maps.append(encoded);t=tracks[(part['id'],index)]
            assert t['principalPointPixels']==[128,96]
            obj=next((o for o in t['objects'] if o['objectId']==20),None)
            center=obj['centerWorld'] if obj else [70,0,30]
            frames.append([0,len(tokens),index,round(center[0]),round(center[2]),256,round(index/96*256/6)])
        oracles[part['id']]=np.stack(images)
        stats.append({'id':part['id'],'first':first,'count':len(frames)-first,
            'sourceFrames':part['frameCount'],'durationSeconds':part['durationSeconds']})
        print(json.dumps({'nativeBackgroundPart':pi,'tiles':len(dictionary)}),flush=True)
    assert len(dictionary)<32768
    payload=bytearray(len(dictionary)*4)
    address=lambda n:0x10000+n if n<0x60000 else 0xb6000+n-0x60000
    def append(data):
        if len(payload)<0x60000<len(payload)+len(data):payload.extend(bytes(0x60000-len(payload)))
        result=address(len(payload));payload.extend(data);return result
    for encoded,index in dictionary.items():struct.pack_into('>I',payload,index*4,append(encoded))
    # Maps use MC68000 word loads; byte-sized tile streams may end unaligned.
    if len(payload)&1:payload.append(0)
    for frame,encoded in zip(frames,maps):frame[0]=append(encoded)
    if len(payload)>0x60000+0x37800:raise ValueError('native512 tiles exceed standard 1MiB layout')
    (out/'background.bin').write_bytes(payload)
    np.savez_compressed(out/'native-background-oracle.npz',**oracles)
    settings['_codec']={'name':'native16-repeat-row-color-run-tiles-v2',
        'dictionaryTiles':len(dictionary),'pointerTableAddress':0x10000,
        'pointerTableBytes':len(dictionary)*4,'mapBytes':sum(map(len,maps)),
        'physicalTilePixels':[16,16],'currentTileIdsAddress':0xed800,
        'unchangedTileDecodeAndTransferSkipped':True,'nativeRaster':True,
        'offlineBackgroundOnly':True,'sourceCameraChanged':False}
    return frames,stats,seq
