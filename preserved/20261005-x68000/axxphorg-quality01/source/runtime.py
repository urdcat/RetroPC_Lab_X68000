"""Operate only this prototype's re-bound shared instance; never start a host.

Connection credentials stay in the upstream local session file and are never
included in receipts. API RAM input is diagnostic, not physical-key acceptance.
"""
from pathlib import Path
from datetime import datetime, timedelta, timezone
import argparse, base64, hashlib, json, os, struct, subprocess, sys, time
sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
HOST = Path('D:/work/DevelopTools/Emulators/Champon8')
OWNER = HOST/'private/shared-host-resume-20261005/owners/x68000.json'
OUT = HERE/'build'
sys.path.insert(0, str(HOST))
from client import Client

def stamp(): return datetime.now(timezone(timedelta(hours=9))).isoformat()
def sha(b): return hashlib.sha256(b).hexdigest()
def save(p, value): p.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n','utf8')

def connect():
    owner=json.loads(OWNER.read_text('utf8')); expected=owner['connection']
    pid=expected['pid']
    process=subprocess.run(['powershell','-NoProfile','-Command',
        f'[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); (Get-Process -Id {pid} -ErrorAction Stop).Path'],capture_output=True,text=True,encoding='utf8')
    if process.returncode: raise RuntimeError('shared_host_missing; coordinate restart, do not launch privately')
    exe=Path(process.stdout.strip())
    if os.path.normcase(str(exe.resolve()))!=os.path.normcase(str(Path(expected['executable']).resolve())):
        raise RuntimeError('shared_host_executable_mismatch')
    if sha(exe.read_bytes())!=expected['executableSha256']: raise RuntimeError('shared_host_hash_mismatch')
    client=Client(expected['sessionFile'])
    if client.session.get('pid')!=pid: raise RuntimeError('shared_session_pid_mismatch')
    workspace=client.workspace()
    if workspace['workspaceId']!=expected['workspaceId']: raise RuntimeError('shared_workspace_mismatch')
    identity={'checkedAt':stamp(),'pid':pid,'version':expected['version'],
        'executable':str(exe),'executableSha256':expected['executableSha256'],
        'workspaceId':workspace['workspaceId'],'sessionReference':expected['sessionFile'],
        'handle':owner['reservedHandle'],'instanceCount':len(workspace['instances']),
        'scope':'Only owned handle; no host shutdown/layout/peer mutation'}
    return client,owner,workspace,identity

def target(create=False):
    client,owner,workspace,identity=connect(); handle=identity['handle']
    descriptor=next((x for x in workspace['instances'] if x['handle']==handle),None)
    path=OUT/'connection.json'
    if descriptor is None:
        if not create: raise RuntimeError('owned_instance_not_created')
        # X68000 RAM-entry needs no cartridge/IPL. Explicit existing ROM root
        # does not alter any shared ROM or shared host setting.
        descriptor=client.workspace({'op':'create','handle':handle,
            'title':'AxxPhorg X68000 rich / runtime 3D prototype',
            'model':'x68000','mode':'RAM','romDirectory':owner['connection']['romDirectory']})
        identity['createdAt']=stamp()
    elif path.exists():
        previous=json.loads(path.read_text('utf8'))
        if previous['workspaceId']!=identity['workspaceId'] or previous['instanceId']!=descriptor['instanceId']:
            raise RuntimeError('owned_instance_rebinding_requires_review')
    else:
        raise RuntimeError('unrecorded_existing_reserved_handle; inspect before adoption')
    identity['instanceId']=descriptor['instanceId']
    identity['model']='x68000';identity['mode']='RAM'
    identity['peerIdentities']=[{'handle':x['handle'],'instanceId':x['instanceId']} for x in workspace['instances'] if x['handle']!=handle]
    save(path,identity)
    return client,client.instance(handle),identity

def read(t, address, length):
    return b''.join(bytes.fromhex(t.request({'op':'read-space','space':'cpu-logical',
        'address':address+i,'length':min(4096,length-i)})['hex']) for i in range(0,length,4096))

def state(t):
    b=read(t,0xf0000,256)
    u16=lambda o: struct.unpack_from('>H',b,o)[0]
    u32=lambda o: struct.unpack_from('>I',b,o)[0]
    return {'ready':u16(0),'tick':u32(4),'pages':u32(8),'part':u16(12),
        'sourceFrame':u16(16),'backgroundIndex':u16(18),'shownPart':u16(20),'shownSourceFrame':u16(22),
        'playerX':struct.unpack_from('>h',b,24)[0],'playerZ':struct.unpack_from('>h',b,26)[0],
        'keys':u16(28),'shotsFired':u32(36),'poses':u32(40),'hits':u32(48),
        'hp':[u16(64+i*2) for i in range(6)],'encounter':u16(80),'defeatEvents':u16(82),
        'visibleFaces':u16(192),'spans':u32(196),'pixels':u32(200),
        'shownBossAngle':u16(208),'shownPrincipalY':u16(210)}

def set_keys(t, mask):
    if not 0<=mask<=127: raise ValueError('mask_range')
    t.request({'op':'pause'})
    t.request({'op':'load','address':0xf0020,'hex':mask.to_bytes(2,'big').hex()})
    return {'classification':'Prototype software mailbox input, not physical keyboard',
        'mask':mask,'at':stamp()}

def advance(t, pages=1, max_slices=200):
    before=state(t); goal=before['pages']+pages; started=time.perf_counter();cycles=0
    for n in range(max_slices):
        r=t.request({'op':'run-until','maxCycles':2_000_000,'maxInstructions':100_000,
            'memory':{'space':'cpu-logical','address':0xf0008,'hex':goal.to_bytes(4,'big').hex()}})
        cycles+=r['executedCycles']
        if r.get('conditionMatched'): break
        if r['stopReason'] not in ('instruction-limit','cycle-limit'):
            raise RuntimeError('unexpected_guest_stop: '+r['stopReason'])
        if n and n%10==0: print(json.dumps({'progress':state(t),'slices':n+1}),flush=True)
    else: raise RuntimeError('finite_page_budget_exhausted')
    return {'before':before,'after':state(t),'executedCycles':cycles,
        'nominalGuestSeconds':cycles/10_000_000,'wallSeconds':time.perf_counter()-started}

def load(t):
    t.request({'op':'pause'})
    mapping=t.request({'op':'address-spaces'})
    segments=[]
    background=(OUT/'background.bin').read_bytes()
    for address,data in [(0x1000,(OUT/'guest.bin').read_bytes()),(0x10000,background[:0x60000]),(0xb6000,background[0x60000:])]:
        for offset in range(0,len(data),65536):
            b=data[offset:offset+65536];segments.append({'address':address+offset,'hex':b.hex(),'sha256':sha(b)})
    zero=bytes(512);segments.append({'address':0xf0000,'hex':zero.hex(),'sha256':sha(zero)})
    result=t.request({'op':'program-load','schema':'champon8.ram-program.v1',
        'model':'x68000','mode':'RAM','addressSpace':'cpu-visible-ram',
        'mappingToken':mapping['mappingToken'],'protectedRanges':[],
        'segments':segments,'entry':{'pc':0x1000,'sp':0xff000}})
    assert result['programLoad']['verified']
    return {k:v for k,v in result['programLoad'].items() if k!='segments'}

def main():
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['inspect','load','advance','resume','pause','capture'])
    parser.add_argument('--pages',type=int,default=4);args=parser.parse_args()
    OUT.mkdir(exist_ok=True)
    if args.action=='inspect':
        client,owner,w,ident=connect();print(json.dumps({**ident,'ownedHandlePresent':any(x['handle']==ident['handle'] for x in w['instances'])},ensure_ascii=True));client.close();return
    client,t,ident=target(create=args.action=='load');receipt={'at':stamp(),'action':args.action,'identity':ident}
    try:
        if args.action=='load': receipt['programLoad']=load(t);receipt['execution']=advance(t,1)
        elif args.action=='advance':
            t.request({'op':'pause'});receipt['execution']=advance(t,args.pages)
        elif args.action in ('resume','pause'):t.request({'op':args.action})
        if args.action in ('load','advance','capture'):
            t.capture(OUT/'latest.png');receipt['state']=state(t)
        receipt['status']=t.request({'op':'status'})
        save(OUT/(args.action+'-receipt.json'),receipt)
        print(json.dumps({k:v for k,v in receipt.items() if k not in ('identity','status')},ensure_ascii=True),flush=True)
    finally:client.close()

if __name__=='__main__':main()
