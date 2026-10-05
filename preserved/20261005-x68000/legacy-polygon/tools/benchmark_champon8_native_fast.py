#!/usr/bin/env python3
"""Build/profile native512 in ONE owned headless host, never in shared tabs.

Generated images/binaries/model-bearing source snapshots remain in ignored build/.
Original and starless reference are assembled from the preserved demo.m68.
Uses emulator-reported cycles, not Windows execution time or hardware timing.
"""
import argparse
import base64
import hashlib
import json
import re
import statistics
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / 'src/platform/x68k/champon8_poly_demo'
OUT = DEMO / 'build/native-fast-20260916'
HOST = Path('D:/work/DevelopTools/Emulators/Champon8')
ASM = Path('D:/work/DevelopTools/Assemblers/m68kasm/dist/m68kasm-0.9.0-preview.1-win-x64/bin/m68kasm.exe')
sys.dont_write_bytecode = True
sys.path.insert(0, str(HOST))
from client import Client

def sha(data):
    return hashlib.sha256(data).hexdigest()

def build(variant):
    source = (DEMO / ('native_fast.m68' if variant == 'fast' else 'demo.m68')).read_text(encoding='utf-8')
    if variant == 'starless':
        start = source.index('  lea starfield(pc),a0', source.index('[draw_backdrop'))
        end = source.index('  move.l draw_vram_base.l,a0', start)
        source = source[:start] + source[end:]
    source = source.replace('include "build/model_generated.inc"', 'include "' + (DEMO / 'build/model_generated.inc').as_posix() + '"')
    folder = OUT / variant
    folder.mkdir(parents=True, exist_ok=True)
    src = folder / 'demo.m68'
    src.write_text(source, encoding='utf-8')
    binary = folder / 'demo.bin'
    result = subprocess.run([str(ASM), 'assemble', str(src), '-o', str(binary), '--cpu', '68000', '--base-address', '0x1000'], capture_output=True, text=True, encoding='utf-8')
    (folder / 'assembler.txt').write_text(result.stdout + result.stderr, encoding='utf-8')
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    symbols = {m[0]: int(m[1], 16) for m in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)', result.stdout, re.M)}
    manifest = {'variant': variant, 'sourceSha256': sha(src.read_bytes()), 'binarySha256': sha(binary.read_bytes()), 'includeSha256': sha((DEMO/'build/model_generated.inc').read_bytes()), 'symbols': symbols}
    (folder / 'build.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return binary.read_bytes(), manifest

def load(target, data):
    target.request({'op': 'pause'})
    mapping = target.request({'op': 'address-spaces'})
    r = target.request({'op': 'program-load', 'schema': 'champon8.ram-program.v1', 'model': 'x68000', 'mode': 'RAM', 'addressSpace': 'cpu-visible-ram', 'mappingToken': mapping['mappingToken'], 'protectedRanges': [], 'segments': [{'address': 0x1000, 'hex': data.hex(), 'sha256': sha(data)}, {'address': 0xF0000, 'hex': bytes(16).hex(), 'sha256': sha(bytes(16))}], 'entry': {'pc': 0x1000, 'sp': 0xFF000}})
    assert r['programLoad']['verified']
    return r

def run(target, **condition):
    r = target.request({'op': 'run-until', 'maxCycles': 30_000_000, 'maxInstructions': 1_000_000, **condition})
    if not r['conditionMatched']:
        raise RuntimeError(str({k: v for k, v in r.items() if k != 'framePng'}))
    return r

def word(target, addr):
    return int(target.request({'op': 'read-space', 'space': 'cpu-logical', 'address': addr, 'length': 2})['hex'], 16)

PHASES = ['prepare_draw_page', 'clear_previous_spans', 'draw_backdrop', 'transform_vertices', 'build_face_order', 'sort_face_order', 'draw_faces', 'commit_bounds', 'present_frame']

def profile(target, variant, binary, manifest, frames):
    folder = OUT / variant
    load(target, binary)
    ready = run(target, memory={'space': 'cpu-logical', 'address': 0xF0004, 'hex': 'a55a'})
    palette = target.request({'op': 'read-space', 'space': 'cpu-logical', 'address': 0xE82000, 'length': 32})['hex']
    assert palette.lower() == ''.join(f'{w:04x}' for w in json.loads((DEMO/'build/manifest.json').read_text(encoding='utf-8'))['screen']['paletteWords'])
    rows, hashes = [], {}
    for frame in range(1, frames+1):
        samples = [run(target, pc=manifest['symbols'][name], captureFrame=(name in ['clear_previous_spans', 'draw_backdrop', 'present_frame'])) for name in PHASES]
        hidden = [s['frameSha256'] for s in samples if 'frameSha256' in s]
        assert len(set(hidden)) == 1, ('visible-page-modified', frame)
        end = run(target, memory={'space': 'cpu-logical', 'address': 0xF0000, 'hex': frame.to_bytes(2, 'big').hex()})
        assert word(target, 0xE82600) == (2 if frame & 1 else 1)
        assert word(target, 0xE88000) & 0x80 == 0
        shown = target.request({'op': 'observe-state', 'captureFrame': True})
        pose = word(target, 0xF0002)
        if pose in hashes:
            assert hashes[pose] == shown['frameSha256'], ('dirty-reuse', frame, pose)
        hashes[pose] = shown['frameSha256']
        if frame <= 64:
            (folder/f'pose-{pose:03}.png').write_bytes(base64.b64decode(shown['framePng']))
        phases = {PHASES[i]: samples[i+1]['executedCycles'] for i in range(len(samples)-1)}
        phases['present_frame'] = end['executedCycles']
        phases['loop_overhead'] = samples[0]['executedCycles']
        rows.append({'frame': frame, 'pose': pose, 'phases': phases, 'total': sum(phases.values()), 'frameSha256': shown['frameSha256']})
        if frame % 16 == 0:
            print(f'{variant}: {frame}/{frames}', flush=True)
    steady = rows[2:]
    means = {name: statistics.fmean(r['phases'][name] for r in steady) for name in rows[0]['phases']}
    avg = statistics.fmean(r['total'] for r in steady)
    report = {'variant': variant, 'binarySha256': sha(binary), 'frames': frames, 'readyCycles': ready['executedCycles'], 'paletteHex': palette, 'phaseMeanCycles': means, 'meanCycles': avg, 'nominalFps10MHz': 1e7/avg, 'minCycles': min(r['total'] for r in steady), 'maxCycles': max(r['total'] for r in steady), 'rows': rows, 'poseHashes': hashes, 'hiddenPagesUnchanged': True, 'flipsInVBlank': True, 'acceptance': 'Champon8 cycles; not hardware or wall-clock performance'}
    if variant == 'fast':
        reference = json.loads((OUT/'starless/profile.json').read_text(encoding='utf-8'))
        for pose, value in hashes.items():
            assert value == reference['poseHashes'][str(pose)], ('reference-image-mismatch', pose)
        report['starlessReferenceMatchingPoses'] = len(hashes)
        report['referenceBinarySha256'] = reference['binarySha256']
    (folder/'profile.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('rows', 'poseHashes')}), flush=True)
    return report

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--variants', nargs='+', default=['original','starless','fast'], choices=['original','starless','fast'])
    p.add_argument('--frames', type=int, default=128)
    p.add_argument('--build-only', action='store_true')
    a = p.parse_args()
    assert 4 <= a.frames <= 128
    OUT.mkdir(parents=True, exist_ok=True)
    builds = {v: build(v) for v in a.variants}
    if a.build_only:
        print(json.dumps({v: b[1]['binarySha256'] for v,b in builds.items()}))
        return
    pointer = json.loads((HOST/'private/ACTIVE_DESKTOP.json').read_text(encoding='utf-8'))
    exe = Path(pointer['executable'])
    assert sha(exe.read_bytes()) == pointer['executableSha256']
    session = OUT/f'headless-{time.time_ns()}.json'
    log = (OUT/'headless.log').open('w')
    process = subprocess.Popen([str(exe), '--headless', '--paused', '--port', '0', '--session-file', str(session), '--roms', pointer['romRoot'], '--handle', 'native-benchmark', '--model', 'x68000', '--mode', 'RAM', '--title', 'Owned isolated native512 benchmark'], stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
    identity = {'pid': process.pid, 'exe': str(exe), 'exeSha256': pointer['executableSha256'], 'session': str(session), 'reason': 'Isolated benchmark; leave shared comparison and every live tab untouched'}
    (OUT/'headless-identity.json').write_text(json.dumps(identity, indent=2), encoding='utf-8')
    client = None
    try:
        for _ in range(100):
            if session.exists():
                break
            if process.poll() is not None:
                raise RuntimeError('headless host exited')
            time.sleep(.1)
        client = Client(session)
        w = client.workspace()
        assert len(w['instances']) == 1
        target = client.instance('native-benchmark')
        print(json.dumps(identity), flush=True)
        for variant, (binary, manifest) in builds.items():
            profile(target, variant, binary, manifest, a.frames)
    finally:
        if client:
            client.close()
        process.terminate()  # Only this Popen-owned headless PID; never shared host.
        process.wait(timeout=10)
        log.close()

if __name__ == '__main__':
    main()
