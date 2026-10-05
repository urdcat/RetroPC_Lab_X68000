"""Bounded storage probe. Native raster is source art, never runtime evidence.

The frozen source renderer is loaded read-only. Only pixel dimensions/focal and
the correspondingly doubled principal point change, not the world camera/paths.
"""
import importlib.util,json,time
from pathlib import Path
import numpy as np
from runtime import OUT,save,sha
SOURCE=Path(json.loads((OUT.parent/'settings.json').read_text('utf8'))['sourceDirectory'])

def row_runs(tile):
    result=bytearray()
    rows=[]
    for row in tile:
        edges=np.r_[0,np.flatnonzero(row[1:]!=row[:-1])+1,16]
        rows.append(bytes(((int(r-l)-1)<<4)|int(row[l]) for l,r in zip(edges[:-1],edges[1:])))
    index=0
    while index<16:
        end=index+1
        while end<16 and rows[end]==rows[index]:end+=1
        result.append(((end-index-1)<<4)|(len(rows[index])-1))
        result.extend(rows[index]);index=end
    return bytes(result)

def main():
    spec=importlib.util.spec_from_file_location('frozen_source_renderer',SOURCE/'render_sequence.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.W,module.H,module.FOCAL=512,384,310.0
    module.principal_point=lambda part,t:(256,192)
    models=module.make_models();dictionary={};maps=0;frames=0;started=time.perf_counter()
    for part,(_,duration) in enumerate(module.PARTS):
        for index in range(0,duration*12,4):
            image,_,_=module.render(part,index/12,models)
            blocks=image.reshape(24,16,32,16).transpose(0,2,1,3).reshape(768,16,16)
            ids=[]
            for block in blocks:
                encoded=row_runs(block)
                ids.append(dictionary.setdefault(encoded,len(dictionary)))
            ids=np.asarray(ids)
            maps+=4*(1+int(np.count_nonzero(ids[1:]!=ids[:-1])))
            frames+=1
        print(json.dumps({'part':part,'frames':frames,'tiles':len(dictionary),
            'tileBytes':sum(map(len,dictionary)),'mapBytes':maps}),flush=True)
    result={'nativeRaster':[512,384],'paletteColors':16,'sampleStride':4,
        'tileBytes':sum(map(len,dictionary)),'pointerBytes':len(dictionary)*4,
        'mapsBytes':maps,'totalBytes':sum(map(len,dictionary))+len(dictionary)*4+maps,
        'capacityBytes':0x60000+0x37800,'seconds':time.perf_counter()-started,
        'sourceRendererSha256':sha((SOURCE/'render_sequence.py').read_bytes()),
        'classification':'offline storage feasibility only; not emulator performance or visual acceptance'}
    save(OUT/'native-background-probe.json',result);print(json.dumps(result),flush=True)

if __name__=='__main__':main()
