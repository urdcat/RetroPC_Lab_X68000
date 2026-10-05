"""Collect QUALITY-02 evidence; do not overwrite QUALITY-01 or edit registries.

Identity/status are read-only. This report does not accept playable performance,
physical input, hardware timing, model aesthetics, or complete-game behavior.
"""
import copy
import json
import subprocess
from pathlib import Path
from runtime import HERE, OUT, target, save, sha, stamp


def document(path):
    return json.loads(Path(path).read_text('utf8'))


def latest(name):
    path = Path(document(OUT / name)['report'])
    return path, document(path)


def artifact(path):
    path = Path(path)
    return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha(path.read_bytes())}


def main():
    m = document(OUT / 'build.json')
    vpath, v = latest('latest-verification.json')
    before_path, before = latest('latest-quality02-before.json')
    after_path, after = latest('latest-quality02-after.json')
    assert v['complete'] and after['complete'] and before['complete']
    assert v['binarySha256'] == after['binarySha256'] == m['binarySha256']
    assert sha((OUT / 'guest.bin').read_bytes()) == m['binarySha256']
    assert sha((OUT / 'background.bin').read_bytes()) == m['backgroundSha256']
    assert len(v['oracles']) == 12 and all(o['mismatches'] == 0 for o in v['oracles'])
    assert before['workload'] == after['workload']
    for name, expected in m['sourceFiles'].items():
        assert sha((HERE / name).read_bytes()) == expected, ('source_drift_after_build', name)
    ui_path = OUT / 'QUALITY02-UI.json'
    ui = document(ui_path)
    assert ui['binarySha256'] == m['binarySha256']
    profiles = []
    pointer = document(OUT / 'latest-quality-profile.json')
    assert pointer['binarySha256'] == m['binarySha256']
    for path in pointer['reports']:
        profile = document(path)
        profile.pop('identity', None)
        profiles.append({'reference': path, 'sample': profile})
    client, guest, identity = target()
    try:
        status = guest.request({'op': 'status'})
    finally:
        client.close()
    report = copy.deepcopy(document(HERE / 'QUALITY-20261005-01.json'))
    report.update(reportId='20261005-x68000-axxphorg-quality-02', reportedAt=stamp(),
        requestId='20261005-axxphorg-quality-repair-02', event='quality_repair_checkpoint',
        displayName='AxxPhorg X68000 rich / native512 fixed-camera quality checkpoint',
        libraryVersion='quality-02-native512-v0.5', status='prototype_with_remaining_issues')
    public = ['guest.m68', 'quality.m68', 'native_bg.m68', 'build.py', 'native_background.py',
        'probe_native_background.py', 'runtime.py', 'controller.py', 'controller.html',
        'settings.json', 'verify.py', 'profile_quality.py', 'profile_pose.py',
        'compare_latency.py', 'capture_quality_ui.py', 'quality02_report.py', 'README.txt', 'RESUME.txt']
    report['source'].update(root=str(HERE), entryPoints=public,
        commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=HERE, text=True).strip(),
        dirty=True, fileHashes=[artifact(HERE / f) for f in public],
        provenanceNotes='Separate owner prototype. Published frozen legacy renderer dependency plus pinned external common procedural art v0.5. Old games/demos and QUALITY-01 remain unchanged. No commercial ROM, extracted reference payload, credentials, or large movies published.')
    report['build'].update(command='bundled Python -B prototypes/axxphorg-rich/build.py',
        artifacts=[artifact(OUT / f) for f in ['guest.bin', 'background.bin', 'guest.m68', 'build.json']],
        toolVersions=[{'name': 'm68kasm', 'version': '0.9.0-preview.1', 'sha256': m['assemblerSha256']}])
    report['contract'].update(scratchMemory=m['memory'])
    report['resources'].update(ramBytes=1048576, expansionRequired=False, memoryMap=m['memory'])
    report['fixedProjection'] = m['projection']
    report['assets'] = {'sourceRoot': m['sourceDirectory'], 'manifestSha256': m['sourceManifestSha256'],
        'background': m['background'], 'foreground': {'runtime3d': True, 'offlinePoseImages': False,
        'materialLighting': m['lighting'], 'localImages': m['localImages']}}
    report['repairs'] = [
        'Reversed mixed/solid background tile dispatch fixed; high-word leakage in tile VRAM address removed',
        'Same camera/FOV/models/colors: background re-rasterized offline at native512x384, not 2x upscaling or SSAA',
        'Repeat-row/color-run tile dictionary and compact map tokens fit standard 1MiB; unchanged tile work remains skipped',
        'Scalar color/run dispatch table and unrolled word/long fills; no pose-image LUT',
        'Non-player pose generation split into one phase/page, at most four complete faces; no removed faces/colors',
        '32KiB staging image: publish only complete poses; previous complete images keep translating',
        'Player snapshot refreshed after background restore and damaged pixels restored again; mid-page key edges are not silently acknowledged',
        'Normal actor tick wrap fixed without changing phase/movement contract']
    report['verification'] = {'report': str(vpath), 'reportSha256': sha(vpath.read_bytes()),
        'checks': v['checks'], 'oracles': v['oracles'], 'classification': v['classification']}
    report['performance'].update(scope='actual completed-page pipeline and displayed motion; not hardware FPS',
        clockBasis='executed CPU cycles / nominal 10MHz', samples=v['measurements'],
        configuration={'model': 'x68000', 'mode': 'RAM', 'backgroundSourceStride': 4,
                       'nativeBackgroundPixels': [512,384]},
        pipelineProfile=profiles[-1]['sample'], pipelineProfileReference=profiles[-1]['reference'],
        pipelineProfiles=profiles,
        knownLimitations=['Background source cap 3Hz', 'Boss mean page throughput 2.91Hz remains unplayable/unaccepted',
            'Representative boss pages: background restore about256ms dominates; foreground blit65-77ms',
            'Shared host wall time differs from executed-cycle time; no hardware calibration'])
    report['performance']['timingModelQualification'].update(
        emulatorVersion=identity['version'], emulatorExecutableSha256=identity['executableSha256'],
        hardwareTimingCalibration='unverified',
        sameCoreComparisonScope='Same input schedule, start position, world camera and source paths. Background raster workload grows4x (2x-enlarged256x192 -> native512x384); NOT an equal-background-payload speed comparison.')
    comparisons = []
    for old, new in zip(before['windows'], after['windows']):
        assert old['name'] == new['name'] and old['releaseVisibleSettles'] and new['releaseVisibleSettles']
        comparisons.append({'scene': new['name'],
            'beforeFirstVisibleMovementMilliseconds': old['firstVisibleMovementMilliseconds'],
            'afterFirstVisibleMovementMilliseconds': new['firstVisibleMovementMilliseconds'],
            'releaseVisibleSettlesBothVersions': True,
            'beforeRecording': artifact(old['continuousRecording']),
            'afterRecording': artifact(new['continuousRecording'])})
    report['sameClockInputComparison'] = {'before': artifact(before_path), 'after': artifact(after_path),
        'workload': after['workload'], 'windows': comparisons,
        'classification': 'Natural held->visible motion->release recordings of actual guest frames; nominal cycles, not wall/physical timing. No scene/time/camera/HP injection. Input is software mailbox only.'}
    report['normalInput'] = {'nativeChampon8Keyboard': 'unconnected / unverified',
        'physicalX68000': 'unverified', 'ownerBrowserControls': 'Normal owned localhost UI controls operated and actual guest captures recorded',
        'uiEvidence': artifact(ui_path), 'observations': [
            {'label': o['label'], 'running': o['running'],
             'shownHeroCenters': sorted({(s['after']['shownPlayerScreenX'],s['after']['shownPlayerScreenY']) for s in o['samples']}),
             'keys': sorted({s['after']['keys'] for s in o['samples']}),
             'shotsRange': [min(s['after']['shotsFired'] for s in o['samples']),max(s['after']['shotsFired'] for s in o['samples'])]}
            for o in ui['observations']],
        'limit': 'Long UI holds reached viewport clamps. The separate finite comparison verifies released settling away from the clamp; do not treat browser input as native hardware input.'}
    report['sharedInstance'] = {k: identity[k] for k in ['checkedAt', 'pid', 'version',
        'executable', 'executableSha256', 'workspaceId', 'handle', 'instanceId']}
    report['displayRunningAtReport'] = status['running']
    for key, refs in [('source',[str(HERE/f) for f in public]),('build',[str(OUT/'build.json')]),
                      ('emulator',[str(vpath)]),('visual',[o['screenshot'] for o in v['oracles']])]:
        report['evidence'][key].update(status='verified_in_stated_scope', references=refs, conditions=v['classification'])
    report['knownIssues'] = ['Boss page rate and pose/background update cadence remain too low for playable acceptance',
        'Battleship throughput regresses from5.66Hz to4.47Hz at the higher native raster resolution',
        'No per-pixel foreground/background depth masking', 'Native keyboard, Human68k wrapper and hardware unverified',
        'Combat/score/victory/audio/full-game flow and human visual acceptance remain incomplete']
    report['workflow'] = {'delivered': True, 'implementation': True, 'ownerVerification': True,
        'coordinatorVerification': False, 'userAcceptance': False, 'playablePerformanceAccepted': False,
        'centralRegistryChanged': False, 'messagesSent': False,
        'preservationPublicationCommit': '878c6aa2213b0295e69a4cd1f788bf09737ef905',
        'inventorySupplementPublicationCommit': '89253a831d2d5913679790d00fdbf7531d86ac37',
        'currentQualityPublication': 'Separate explicitly staged checkpoint; report records pre-publication parent SHA, not a circular self-commit hash'}
    report['optimizationAdaptationNotes'].update(
        preservedOptimizations=['unchanged tile skip','completed-image caching','partial damage','double buffering','fixed camera'],
        changedFineDetails=report['repairs'], performanceReason='Repair real tile corruption and native resolution while reducing the six-pose input barrier; remaining dominant background transfer/restore is measured, not accepted',
        behaviorAndAppearanceImpact='No camera staging, color sacrifice or mesh thinning. This is a quality/latency checkpoint, not a finished high-FPS game.')
    report['requestedNextStep'] = 'Review actual native512 boss and input recordings; optimize background restore/foreground transfer without reducing colors or model completeness. Native-input and full-game acceptance remain separate.'
    save(HERE / 'QUALITY-20261005-02.json', report)
    print(str(HERE / 'QUALITY-20261005-02.json'))


if __name__ == '__main__':
    main()
