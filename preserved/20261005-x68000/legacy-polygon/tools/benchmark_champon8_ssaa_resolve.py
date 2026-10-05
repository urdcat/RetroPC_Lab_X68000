#!/usr/bin/env python3
"""Isolated SSAA comparison; existing displayed native512 and peers untouched."""
import argparse
import base64
import io
import json
import re
import statistics
import subprocess
import time
from pathlib import Path
from PIL import Image
from benchmark_champon8_native_fast import ASM, Client, DEMO, HOST, OUT, run, sha, word
from verify_champon8_ssaa_demo import load_artifacts, verify_resolve

CHECKPOINTS = ('clear_previous_spans','draw_backdrop','draw_faces','resolve_aa','present_frame')
PHASES = ('frameSetup','dirtyClear','geometryBackdropSort','drawFaces','resolveAA','present')

def profile(target, variant, manifest, binary, lookup, oracle):
    folder = OUT / variant
    folder.mkdir(parents=True, exist_ok=True)
    target.request({'op':'pause'})
    mapping = target.request({'op':'address-spaces'})
    segments = [(0x1000, binary), (0x40000, lookup), (0xF0000, bytes(16))]
    loaded = target.request({'op':'program-load','schema':'champon8.ram-program.v1','model':'x68000','mode':'RAM','addressSpace':'cpu-visible-ram','mappingToken':mapping['mappingToken'],'protectedRanges':[],'segments':[{'address':address+offset,'hex':data[offset:offset+65536].hex(),'sha256':sha(data[offset:offset+65536])} for address,data in segments for offset in range(0,len(data),65536)],'entry':{'pc':0x1000,'sp':0xFF000}})
    assert loaded['programLoad']['verified']
    run(target,memory={'space':'cpu-logical','address':0xF0004,'hex':'a55a'})
    rows=[]
    reference = json.loads((OUT/'ssaa-current/profile.json').read_text(encoding='utf-8')) if variant != 'ssaa-current' else None
    for frame in range(1,129):
        samples=[run(target,pc=manifest['symbols'][name],captureFrame=True) for name in CHECKPOINTS]
        # Shared preview.19 reports a raw 512 image: compare ONLY the intended
        # 256 viewport. Its lower half is not a valid SSAA front-buffer view.
        visible=[]
        for sample in samples:
            picture=Image.open(io.BytesIO(base64.b64decode(sample['framePng']))).convert('RGB')
            visible.append(sha(picture.crop((0,0,256,256)).tobytes()))
        assert len(set(visible))==1, ('SSAA-visible-area-changed',frame)
        verified, rgb, _ = verify_resolve(target,manifest['symbols'],oracle,frame & 1)
        end=run(target,memory={'space':'cpu-logical','address':0xF0000,'hex':frame.to_bytes(2,'big').hex()})
        assert word(target,0xE8001A)==(frame & 1)*256 and word(target,0xE88000)&0x80==0
        pose=word(target,0xF0002)
        phases=dict(zip(PHASES,[v['executedCycles'] for v in samples]+[end['executedCycles']]))
        row={'frame':frame,'pose':pose,'phases':phases,'total':sum(phases.values()),'samplesSha256':verified['sampleBufferSha256'],'outputSha256':verified['outputWordsSha256'],'resolvedPixels':verified['guestReportedResolvedPixels']}
        if reference:
            for key in ('pose','samplesSha256','outputSha256'):
                assert row[key]==reference['rows'][frame-1][key], ('reference-mismatch',frame,key)
        rows.append(row)
        if frame==11:
            Image.frombytes('RGB',(256,256),rgb).save(folder/'pose-080.png')
        if frame%16==0:
            print(f'{variant}: {frame}/128',flush=True)
    steady=rows[2:]
    avg=statistics.fmean(r['total'] for r in steady)
    report={'variant':variant,'binarySha256':sha(binary),'frames':128,'meanCycles':avg,'nominalFps10MHz':1e7/avg,'phaseMeanCycles':{k:statistics.fmean(r['phases'][k] for r in steady) for k in PHASES},'checkedOutputPixels':128*65536,'matchingReferenceFrames':128 if reference else None,'hiddenViewportUnchanged':True,'flipsInVBlank':True,'rows':rows,'note':'Same preview.19 core; display viewport on this host is not SSAA-ready. No live tab replacement.'}
    (folder/'profile.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='rows'}),flush=True)

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--variant',choices=['ssaa-current','ssaa-resolve-fast'],default='ssaa-current')
    args=p.parse_args()
    manifest,binary,lookup,oracle=load_artifacts(DEMO/'build/ssaa/manifest.json')
    folder=OUT/args.variant
    folder.mkdir(parents=True,exist_ok=True)
    if args.variant=='ssaa-resolve-fast':
        source=(DEMO/'ssaa_resolve_fast.m68').read_text(encoding='utf-8').replace('include "build/ssaa/model_generated.inc"','include "'+(DEMO/'build/ssaa/model_generated.inc').as_posix()+'"')
        src=folder/'demo.m68'
        src.write_text(source,encoding='utf-8')
        binpath=folder/'demo.bin'
        result=subprocess.run([str(ASM),'assemble',str(src),'-o',str(binpath),'--cpu','68000','--base-address','0x1000'],capture_output=True,text=True,encoding='utf-8')
        (folder/'assembler.txt').write_text(result.stdout+result.stderr,encoding='utf-8')
        if result.returncode:
            raise RuntimeError(result.stdout+result.stderr)
        binary=binpath.read_bytes()
        manifest={**manifest,'symbols':{m[0]:int(m[1],16) for m in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)',result.stdout,re.M)}}
    pointer=json.loads((HOST/'private/ACTIVE_DESKTOP.json').read_text(encoding='utf-8'))
    exe=Path(pointer['executable'])
    assert sha(exe.read_bytes())==pointer['executableSha256']
    session=folder/f'headless-{time.time_ns()}.json'
    log=(folder/'headless.log').open('w')
    process=subprocess.Popen([str(exe),'--headless','--paused','--port','0','--session-file',str(session),'--roms',pointer['romRoot'],'--handle','ssaa-benchmark','--model','x68000','--mode','RAM'],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
    identity={'pid':process.pid,'session':str(session),'exeSha256':pointer['executableSha256'],'reason':'Owned SSAA baseline/resolve benchmark; no change to shared host'}
    (folder/'identity.json').write_text(json.dumps(identity,indent=2),encoding='utf-8')
    client=None
    try:
        for _ in range(100):
            if session.exists(): break
            if process.poll() is not None: raise RuntimeError('headless exited')
            time.sleep(.1)
        client=Client(session)
        assert len(client.workspace()['instances'])==1
        profile(client.instance('ssaa-benchmark'),args.variant,manifest,binary,lookup,oracle)
    finally:
        if client: client.close()
        process.terminate()
        process.wait(timeout=10)
        log.close()

if __name__=='__main__':
    main()
