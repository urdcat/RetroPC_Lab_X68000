"""Write this owner's QUALITY evidence only; never publish or edit a registry."""
import json,subprocess
from pathlib import Path
from runtime import HERE,OUT,save,sha,stamp,target

def main():
    m=json.loads((OUT/'build.json').read_text('utf8'))
    vpath=Path(json.loads((OUT/'latest-verification.json').read_text('utf8'))['report'])
    v=json.loads(vpath.read_text('utf8'))
    assert v['complete'] and v['binarySha256']==m['binarySha256']
    report=json.loads(Path('D:/work/PolygonGames/templates/library-report.json').read_text('utf8'))
    c,t,identity=target()
    try:status=t.request({'op':'status'})
    finally:c.close()
    report.update(reportId='20261005-x68000-axxphorg-quality-01',event='quality_repair',reportedAt=stamp(),
        requestId='20261005-axxphorg-quality-repair-01',targetId='x68000-ram',displayName='AxxPhorg X68000 rich / fixed projection v0.5',
        familyId='x68000',libraryId='axxphorg-rich',libraryVersion='quality-01-v0.5',status='prototype_with_remaining_issues')
    report['owner'].update(threadId='019f8c98-cf3b-7143-9b4b-5534fa200976',title='X68000 D-side owner')
    commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=HERE,text=True).strip()
    files=['guest.m68','quality.m68','build.py','runtime.py','controller.py','controller.html','settings.json','verify.py','profile_quality.py','capture_quality_ui.py','quality_report.py']
    report['source'].update(root=str(HERE),entryPoints=files,commit=commit,dirty=True,
        fileHashes=[{'path':str(HERE/f),'sha256':sha((HERE/f).read_bytes())} for f in files],
        provenanceNotes='Separate owner prototype. Preserved legacy demos and quality-before-20261005 checkpoint. Original procedural tetrahedral/octahedral rich meshes; shared v0.5 backgrounds. No original commercial artwork acquired.')
    report['build'].update(workingDirectory=str(HERE.parents[1]),command='bundled Python -B prototypes/axxphorg-rich/build.py',
        toolVersions=[{'name':'m68kasm','version':'0.9.0-preview.1','sha256':m['assemblerSha256']}],
        artifacts=[{'path':str(OUT/f),'sha256':sha((OUT/f).read_bytes())} for f in ['guest.bin','background.bin','build.json','guest.m68']])
    report['capabilities'].update(runtimeTransform3d=True,runtimeProjection=True,filledTriangles=True,nearPlaneClipping=False,
        viewportClipping=True,hiddenSurfaceMethod='backface rejection and painter sorting in local images; far-to-near foreground objects; NO per-pixel background depth mask',
        numericFormatAndRange='MC68000 signed integer / Q14 camera trig, truncating perspective division',renderModes=['512x512 256-color two-page GVRAM'])
    report['contract'].update(document=str(Path('D:/work/PolygonGames/proposals/20261005-axxphorg-six-part-background-v05/README.md')),
        inputLayout='word 0xf0020 bits left/right/forward/backward/fire/next/playlist; owner software bridge only',
        registerClobbers='worker functions share scratch; service preserves registers and CCR',stackBytes=None,
        scratchMemory=m['memory'],interruptAndReentry='SR=0x2700; cooperative GPIP bit4 polling, not reentrant',errorBehavior='shared identity mismatch stops; no private emulator fallback')
    report['resources'].update(cpuConfiguration='MC68000 nominal 10MHz, shared Champon8 preview.31',ramBytes=1048576,vramBytes=524288,romBytes=0,
        memoryMap=m['memory'],expansionRequired=False,dmaAndDisplayContention='CPU GVRAM writes; no DMAC/sprite backend; timing uncalibrated',inputAudioBudget='no game audio implementation')
    report['fixedProjection']=m['projection']
    report['assets']={'sourceRoot':m['sourceDirectory'],'manifestSha256':m['sourceManifestSha256'],'background':m['background'],
        'foreground':{'runtime3d':True,'offlinePoseImages':False,'materialLighting':m['lighting'],'localImages':m['localImages']}}
    report['repairs']=['Per-page dirty tiles instead of wide dirty row unions','Direct LUT-expanded background tile writes without an expanded RAM shadow',
        'All changed foreground images completed for the page snapshot','Clear only previously occupied local image spans',
        'Larger native local image arenas prevent old 64x64 cropping','Per-page last-image generation and occupied bbox damage',
        'Fixed v0.5 projection throughout scenes; depth-dependent viewport boundary prevents player disappearance',
        'Input queue preserves press/release edges; stop button no longer generates left input',
        'Controller reconnects expired request channels with immutable identity/code pin checks; idle held-key release watchdog']
    for key,refs in [('source',[str(HERE/f) for f in files]),('build',[str(OUT/'build.json')]),('emulator',[str(vpath)]),
                     ('visual',[o['screenshot'] for o in v['oracles']])]:
        report['evidence'][key].update(status='verified_in_stated_scope',references=refs,conditions=v['classification'])
    report['evidence']['hardware'].update(status='unverified',references=[],conditions='RAM diagnostic only, no IPL/IOCS/Human68k .X acceptance')
    report['performance'].update(scope='bounded full-page pipeline, not hardware FPS',clockBasis='executed CPU cycles / nominal 10MHz',
        samples=v['measurements'],configuration={'model':'x68000','mode':'RAM','backgroundSourceStride':4},
        knownLimitations=['Background sampled at 3Hz ceiling','Boss full-page throughput is not accepted as sufficiently playable','Shared host wall time is separate'])
    report['performance']['timingModelQualification'].update(emulatorName='Champon8',emulatorVersion=identity['version'],emulatorExecutableSha256=identity['executableSha256'],
        hardwareTimingCalibration='unverified',sameCoreComparisonScope='v0.4/v0.5 composition and cache policy differ; no direct speedup ratio asserted')
    profile_pointer=OUT/'latest-quality-profile.json'
    if profile_pointer.exists():
        pointer=json.loads(profile_pointer.read_text('utf8'))
        if pointer['binarySha256']==m['binarySha256']:
            report['performance']['pipelineProfile']=json.loads(Path(pointer['report']).read_text('utf8'))
            report['performance']['pipelineProfile'].pop('identity',None)
            report['performance']['pipelineProfileReference']=pointer['report']
            profiles=[]
            for profile_path in pointer.get('reports',[pointer['report']]):
                sample=json.loads(Path(profile_path).read_text('utf8'))
                sample.pop('identity',None)
                profiles.append({'reference':profile_path,'sample':sample})
            report['performance']['pipelineProfiles']=profiles
    report['verification']={'report':str(vpath),'checks':v['checks'],'oracles':v['oracles']}
    report['sharedInstance']={k:identity[k] for k in ['checkedAt','pid','version','executable','executableSha256','workspaceId','handle','instanceId']}
    report['displayRunningAtReport']=status['running']
    ui=OUT/'QUALITY-UI.json'
    report['normalInput']={'nativeChampon8Keyboard':'unconnected / unverified','physicalX68000':'unverified',
        'ownerBrowserControls':'see QUALITY-UI.json, actual guest capture; not native keyboard acceptance','uiEvidence':str(ui) if ui.exists() else None}
    visual_input_notes=OUT/'QUALITY-UI-OBSERVATIONS.json'
    if visual_input_notes.exists():
        report['normalInput']['visualObservations']=json.loads(visual_input_notes.read_text('utf8'))
        report['normalInput']['visualObservationsReference']=str(visual_input_notes)
    report['knownIssues']=['Native X68000 keyboard path needs upstream emulator/adapter integration','Foreground/background per-pixel depth occlusion is not implemented',
        'Boss page rate / input-to-visible latency remains low','No normal-enemy combat, score, defeat continuation, audio or Human68k executable wrapper',
        'No real-hardware verification or human quality acceptance']
    report['workflow']={'delivered':True,'implementation':True,'ownerVerification':True,'coordinatorVerification':False,'userAcceptance':False,
                        'centralRegistryChanged':False,'messagesSent':False,'commitOrPush':False}
    report['optimizationAdaptationNotes'].update(preservedOptimizations=['immutable background dictionary, unchanged tile skipping, image caching, clipped spans, partial restoration, double buffering'],
        changedFineDetails=['row unions replaced by tile restoration','old intermediate-pair skipping replaced by coherent dirty-tile writes',
                            'one global pose per page replaced by completing changed snapshot poses'],
        performanceReason='Quality repair; eliminate stale/misplaced/cropped images without blank full-screen redraws; retain unresolved throughput targets',
        behaviorAndAppearanceImpact='fixed camera and richer complete models; unapproved camera animation removed')
    report['requestedNextStep']='Coordinator review of actual images and remaining native-input/throughput/depth issues; user visual acceptance remains separate.'
    save(HERE/'QUALITY-20261005-01.json',report)
    print(str(HERE/'QUALITY-20261005-01.json'))

if __name__=='__main__':main()
