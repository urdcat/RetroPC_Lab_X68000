"""v2 consumer layered over the preserved v1 builder; no v1 source edits."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import zipfile
import build_champon8_reproduction as v1

ROOT=v1.ROOT
OUT=v1.DEMO/'build/auto-cycle-20260917'
PACKAGE=Path('D:/work/PolygonGames/specs/20260917-pyuta-auto-cycle-v2')


def package():
    assert v1.sha(PACKAGE/'manifest.json')=='3760924c66233f5894d05edc1f688b762acb4a875c0c554178350c64fc2dab0a'
    assert v1.sha(PACKAGE/'package.zip')=='5af0b299c4cb0dd01651fc34d76a6442a74bfd6f6b9898677bf68c98844be363'
    m=json.loads((PACKAGE/'manifest.json').read_text('utf8'))
    for f in m['files']:assert v1.sha(PACKAGE/f['path'])==f['sha256']
    with zipfile.ZipFile(PACKAGE/'package.zip') as z:
        for name in ['manifest.json']+[f['path'] for f in m['files']]:
            member=next(n for n in z.namelist() if n.endswith('/'+name) or n==name)
            assert z.read(member)==(PACKAGE/name).read_bytes()
    return m


def build(output=OUT,replay=1,choice=0):
    manifest=package();base=v1.build(output/'base-v1',replay,choice)
    source=(output/'base-v1/guest.m68').read_text('utf8')
    original=v1.blocks(source)
    logic=(v1.DEMO/'auto_cycle.m68').read_text('utf8')
    extras=[]
    for name,body in v1.blocks(logic).items():
        if name in v1.blocks(source):source=v1.replace_block(source,name,body)
        else:extras.append(body)
    source=logic[:logic.index('[')]+source
    def edit(name,old,new):
        nonlocal source
        body=v1.blocks(source)[name];assert old in body,(name,old)
        source=v1.replace_block(source,name,body.replace(old,new,1))
    edit('reset_game','moveq #23,d0.l','moveq #33,d0.l')
    edit('reset_game','  bsr.w update_world','  bsr.w cycle_reset\n  bsr.w update_world')
    edit('advance_state','.wave:\n','.wave:\n  tst.w cycle_loop_request.l\n  beq.w .normal_wave\n  bsr.w cycle_auto_reset\n  bra.w .done\n.normal_wave:\n')
    edit('service','  bsr.w input_service','  bsr.w input_service\n  bsr.w cycle_tick')
    edit('service','  bsr.w publish_page','  bsr.w publish_page\n  bsr.w cycle_published')
    edit('service','  bsr.w start_composite','  bsr.w cycle_can_compose\n  tst.w d0.w\n  beq.w .compose\n  bsr.w start_composite')
    edit('update_bullets','  tst.w S_INTRO.l\n  beq.w clear_bullets','  cmpi.w #1,cycle_phase.l\n  bne.w clear_bullets')
    edit('generate_pose','  move.l job_context.l,a6','  move.l job_context.l,a6\n  move.w cycle_phase.l,120(a6)\n  move.w cycle_age.l,122(a6)\n  move.w cycle_epoch.l,124(a6)')
    edit('generate_pose','  move.l job_context.l,a6\n  addq.w #1,30(a6)','  bsr.w cycle_job_accept\n  tst.w d0.w\n  beq.w .cycle_discard\n  move.l job_context.l,a6\n  addq.w #1,30(a6)')
    edit('generate_pose','.invalid:\n','.cycle_discard:\n  addq.l #1,cycle_stale_jobs.l\n  bra.w .done\n.invalid:\n')
    edit('make_snapshots','  move.l 16(a0),d0.l','  move.w 124(a0),d0.w\n  cmp.w cycle_epoch.l,d0.w\n  beq.w .current_pending\n  clr.w 24(a0)\n  bra.w .no_new\n.current_pending:\n  move.l 16(a0),d0.l')
    edit('make_snapshots','  move.l 90(a0),86(a0)','  move.l 90(a0),86(a0)\n  move.w 120(a0),134(a0)\n  move.w 122(a0),136(a0)\n  move.w 124(a0),138(a0)\n  move.w 32(a0),140(a0)\n  move.w 34(a0),142(a0)\n  move.w 36(a0),144(a0)')
    edit('start_composite','  bsr.w make_snapshots','  bsr.w make_snapshots\n  bsr.w cycle_capture_page')
    # Two physical restore rows keep polling gaps below the MFP blank pulse.
    # Four full-width rows could skip logical ticks during phase invalidation.
    edit('composite_row','  cmpi.w #4,d7.w','  cmpi.w #2,d7.w')
    edit('composite_row','  moveq #4,d7.l','  moveq #2,d7.l')
    boss=original['boss_snapshot']
    boss=boss[:boss.index('.held:')]+'.held:\n  bsr.w cycle_pose\n'+boss[boss.index('.center:'):]
    source=v1.replace_block(source,'boss_snapshot',boss)
    mask=original['prepare_mask'];mask='''[prepare_mask
  clr.w mask_snapshot.l
  clr.w mask_row.l
  bsr.w cycle_mask_level
  cmpi.w #-1,d0.w
  bne.w .level
  moveq #16,d0.l
.level:
  move.w d0.w,S_MASKLEVEL.l
  move.w d0.w,mask_level_snapshot.l
  cmpi.w #16,d0.w
  beq.w .damage
  move.w #1,mask_snapshot.l
'''+mask[mask.index('.damage:'):]
    mask=mask.replace('.done:\n]','.done:\n  bsr.w cycle_full_rectangle\n]')
    source=v1.replace_block(source,'prepare_mask',mask)
    # New hooks can enlarge short branches without altering the old source.
    for name in ['service','advance_state','make_snapshots']:
        body=v1.blocks(source)[name];body=re.sub(r'\b(b[a-z]+)\.s ',r'\1.w ',body)
        source=v1.replace_block(source,name,body)
    source=source.replace('include "'+(output/'base-v1/renderer.inc').as_posix()+'"','\n'.join(extras)+'\ninclude "'+(output/'base-v1/renderer.inc').as_posix()+'"')
    (output/'guest.m68').write_text(source,'utf8')
    result=subprocess.run([str(v1.cache.ASM),'assemble',str(output/'guest.m68'),'-o',str(output/'guest.bin'),'--cpu','68000','--base-address','0x1000'],capture_output=True,text=True,encoding='utf8')
    (output/'assembler.txt').write_text(result.stdout+result.stderr,'utf8')
    if result.returncode:raise RuntimeError(result.stdout+result.stderr)
    symbols={k:int(v,16) for k,v in re.findall(r'^\s*(\S+)\s*=\s*\$([0-9a-fA-F]+)',result.stdout,re.M)}
    assert (output/'guest.bin').stat().st_size+0x1000<0x8000
    m={**base,'schema':'x68000-pyuta-auto-cycle.v2','baseSpecId':base['specId'],'specId':manifest['specId'],'cycleManifest':manifest,
       'symbols':symbols,'cycleLogicSha256':v1.sha(v1.DEMO/'auto_cycle.m68'),'builderSha256':v1.sha(Path(__file__)),
       'binarySha256':v1.sha(output/'guest.bin'),'cycleDwellTicks':832,'animationTickAdaptation':'256/64/256 at native55.45Hz; dwell ceil15seconds separately'}
    (output/'build.json').write_text(json.dumps(m,indent=2),'utf8')
    print(json.dumps({'binary':str(output/'guest.bin'),'bytes':(output/'guest.bin').stat().st_size,'sha256':m['binarySha256']}),flush=True)
    return m


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=OUT);p.add_argument('--choice',type=int,default=0);p.add_argument('--replay',type=int,default=1);a=p.parse_args();build(a.output.resolve(),a.replay,a.choice)
