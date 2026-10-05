"""Finite owned-instance verification, independent background/compositor oracle.

Only software-input masks are changed during the natural sequence. No phase,
time or artwork injection is used for that run. Collision fixtures are separate.
No host process or peer tab is created, closed, paused or configured here.
"""
import argparse, json, math, struct, time
from pathlib import Path
import numpy as np
from build import SOURCE
from runtime import OUT, HERE, target, load, advance, set_keys, state, read, save, stamp, sha

def word(t,a):return int.from_bytes(read(t,a,2),'big')
def signed(t,a):return int.from_bytes(read(t,a,2),'big',signed=True)
def trunc(n,d):return abs(n)//d * (-1 if n<0 else 1)
def sine(a):return round(math.sin(a*math.tau/256)*16384)

def projection_check(t,m):
    s=m['symbols']; model_pointer=int.from_bytes(read(t,s['render_model'],4),'big')
    name=next(n for n in m['models'] if s[n+'_vertices']==model_pointer)
    model=m['models'][name];angle=word(t,s['current_angle'])&255
    depth=signed(t,s['pose_depth']);vx=signed(t,s['pose_view_x']);vy=signed(t,s['pose_view_y'])
    expected=[]
    for x,y,z in model['vertices']:
        xx=(x*sine(angle+64)+z*sine(angle))>>14
        zz=(z*sine(angle+64)-x*sine(angle))>>14
        yy=(y*12551-zz*10531)>>14
        dz=(y*10531+zz*12551)>>14
        d=max(64,depth+dz)
        center=word(t,s['local_center'])
        expected.append((trunc(xx*310-vx*dz,d)+center,trunc(yy*310-vy*dz,d)+center,d))
    actual=list(struct.iter_unpack('>hhh',read(t,s['projected_vertices'],6*len(expected))))
    assert actual==expected, ('vertex_projection',name,actual[:2],expected[:2])
    return {'model':name,'vertices':len(expected),'exactQ14Projection':True}

def oracle(t,m,source,folder,label):
    st=state(t);s=m['symbols'];part=m['parts'][st['shownPart']]['id'];idx=st['shownSourceFrame']
    assert st['shownPrincipalY']==256,('fixed_projection',label,st['shownPrincipalY'])
    ids=np.frombuffer(read(t,0xed800,1536),dtype='>u2')
    packed=np.frombuffer((OUT/'background.bin').read_bytes()[:m['background']['codec']['dictionaryBytes']],dtype=np.uint8).reshape(-1,32)
    tiles=np.empty((len(packed),64),dtype=np.uint8);tiles[:,::2]=packed>>4;tiles[:,1::2]=packed&15
    decoded=tiles[ids].reshape(24,32,8,8).transpose(0,2,1,3).reshape(192,256)
    shadow=np.repeat(np.repeat(decoded,2,axis=0),2,axis=1)
    expected_bg=np.repeat(np.repeat(source[part][idx],2,axis=0),2,axis=1)
    count=int(np.count_nonzero(shadow!=expected_bg))
    assert count==0,('background_source',label,count,part,idx)
    expected=np.zeros((512,512),dtype=np.uint16);expected[64:448]=shadow
    objects=list(struct.iter_unpack('>8h',read(t,0xef000,7*16)))
    order=list(struct.unpack('>7H',read(t,s['object_order'],14)))
    assert sorted(order)==list(range(7))
    assert all(objects[order[i]][2]>=objects[order[i+1]][2] for i in range(6))
    opaque=[]
    def spark(x,y,color):
        if 0<=x<510:
            for row in range(y,y+3):
                if 64<=row<448:expected[row,x:x+3]=color
    for slot in order:
        x,y,depth,kind,angle,valid,_,_=objects[slot]
        if kind<0 or not valid:continue
        desc=m['localImages'][slot];size=desc['size'];center=size//2
        image=np.frombuffer(read(t,desc['address'],size*size*2),dtype='>u2').reshape(size,size)
        bounds=np.frombuffer(read(t,desc['boundsAddress'],size*4),dtype='>u2').reshape(2,size)
        for row in range(size):
            covered=np.flatnonzero(image[row]&256)
            if len(covered):assert bounds[0,row]<=int(covered.min()) and bounds[1,row]>int(covered.max())
        dx=x-center;dy=y-center
        lo_x=max(0,-dx);hi_x=min(size,512-dx);lo_y=max(0,64-dy);hi_y=min(size,448-dy)
        if hi_x>lo_x and hi_y>lo_y:
            a=image[lo_y:hi_y,lo_x:hi_x];dst=expected[dy+lo_y:dy+hi_y,dx+lo_x:dx+hi_x]
            np.copyto(dst,a&255,where=(a&256)!=0)
        spark(x,y+18,90)
        opaque.append({'slot':slot,'kind':kind,'opaquePixels':int(np.count_nonzero(image&256))})
    for x,y,valid in struct.iter_unpack('>hhH',read(t,0xefe00,48)):
        if valid:spark(x,y,88)
    age=signed(t,s['shown_effect_age'])
    if age>=0:
        x=signed(t,s['hit_x']);y=signed(t,s['hit_y']);spark(x,y,89)
        for i in range(6):spark(x+(sine(i*42)*age>>14),y+(sine(i*42+64)*age>>14),91)
    page=word(t,0xe82600);assert page in (1,2)
    actual=np.frombuffer(read(t,0xc00000 if page==1 else 0xc80000,524288),dtype='>u2').reshape(512,512)
    mismatch=int(np.count_nonzero(actual!=expected))
    if mismatch:
        yy,xx=np.argwhere(actual!=expected)[0];raise AssertionError(('page_composition',label,mismatch,int(xx),int(yy),int(actual[yy,xx]),int(expected[yy,xx])))
    t.capture(folder/(label+'.png'))
    from PIL import Image
    colors=Image.open(folder/(label+'.png')).convert('RGB').getcolors(262144)
    result={'label':label,'state':st,'backgroundPixelsCompared':196608,
        'pagePixelsCompared':262144,'mismatches':0,'projection':projection_check(t,m),
        'uniqueScreenColors':len(colors),'opaqueObjects':opaque,
        'foregroundOrder':'far-to-near local objects; no per-pixel background depth mask',
        'screenshot':str(folder/(label+'.png'))}
    save(folder/(label+'.json'),result);print(json.dumps({'verified':label,'part':st['part'],'pose':idx,'colors':len(colors)},ensure_ascii=True),flush=True)
    return result

def wait_for(t,predicate,limit=1500):
    cycles=0;first=state(t)
    for n in range(limit):
        if predicate(state(t)):return {'before':first,'after':state(t),'cycles':cycles,'pages':n}
        cycles+=advance(t,1)['executedCycles']
        if n and n%100==0:print(json.dumps({'naturalProgress':state(t)}),flush=True)
    raise RuntimeError('finite_natural_sequence_budget')

def main():
    p=argparse.ArgumentParser();p.add_argument('--quick',action='store_true');a=p.parse_args()
    folder=OUT/('verification-'+str(time.time_ns()));folder.mkdir()
    m=json.loads((OUT/'build.json').read_text('utf8'));npz=np.load(SOURCE/'media/indexed-source-frames.npz')
    source={part['id']:npz[part['id']] for part in m['parts']}
    client,t,ident=target();report={'schema':'axxphorg-x68000-rich-verification.v1','at':stamp(),
        'identity':ident,'binarySha256':m['binarySha256'],'sourceManifestSha256':m['sourceManifestSha256'],
        'classification':'Owned shared-host bounded execution; software mailbox input; not physical keys/hardware',
        'oracles':[],'measurements':[],'checks':{},'complete':False}
    try:
        assert sha((OUT/'guest.bin').read_bytes())==m['binarySha256']
        load(t);advance(t,1)
        report['oracles'].append(oracle(t,m,source,folder,'stars-start'))
        start=state(t);set_keys(t,18);right=advance(t,4);right_state=state(t)
        assert right_state['playerX']>start['playerX'] and right_state['shotsFired']>start['shotsFired']
        set_keys(t,1);left=advance(t,4);left_state=state(t)
        assert left_state['playerX']<right_state['playerX']
        set_keys(t,0);advance(t,2);released=state(t);advance(t,2)
        assert state(t)['playerX']==released['playerX']
        report['checks']['heldMoveFireRelease']={'right':right,'left':left,'released':released,
            'physicalKeyboard':False,'phaseTimeInjection':False}
        report['checks']['viewportHeldDirections']=[]
        for label,mask in [('right-forward',6),('right-backward',10),('left-backward',9),('left-forward',5)]:
            set_keys(t,mask);advance(t,18);set_keys(t,0);advance(t,1)
            objects=list(struct.iter_unpack('>8h',read(t,0xef000,112)))
            assert 94<=objects[0][0]<=418,('player_viewport',label,objects[0])
            report['checks']['viewportHeldDirections'].append({'label':label,'state':state(t),'displayedPlayer':objects[0]})
            report['oracles'].append(oracle(t,m,source,folder,'input-'+label))
        measure=advance(t,28);measure['label']='stars steady, software input released'
        measure['pageHz']=28/measure['nominalGuestSeconds'];report['measurements'].append(measure)
        report['oracles'].append(oracle(t,m,source,folder,'stars-steady'))
        set_keys(t,32);advance(t,1);set_keys(t,0)
        if a.quick:
            report['complete']=True;return
        wait_for(t,lambda q:q['part']==2 and q['shownSourceFrame']>=64)
        report['oracles'].append(oracle(t,m,source,folder,'battleship-near'))
        objects=list(struct.iter_unpack('>8h',read(t,0xef000,112)))
        assert sum(o[3]==1 for o in objects)<=1
        report['checks']['battleshipEnemyLimit']=1
        measure=advance(t,10);measure['label']='battleship near';measure['pageHz']=10/measure['nominalGuestSeconds'];report['measurements'].append(measure)
        wait_for(t,lambda q:q['part']==3 and q['shownSourceFrame']>=48)
        report['oracles'].append(oracle(t,m,source,folder,'boss-arrival'))
        objects=list(struct.iter_unpack('>8h',read(t,0xef000,112)))
        assert not any(o[3]==1 for o in objects)
        wait_for(t,lambda q:q['part']==4 and q['shownSourceFrame']>=12)
        report['oracles'].append(oracle(t,m,source,folder,'boss-hold'))
        hp=state(t)['hp'];assert hp==[m['settings']['bossPartHp']]*6
        measure=advance(t,20);measure['label']='boss holding';measure['pageHz']=20/measure['nominalGuestSeconds'];report['measurements'].append(measure)
        wait_for(t,lambda q:q['part']==4 and q['sourceFrame']>=110)
        assert state(t)['hp']==hp
        report['checks']['hpPersistsAtSixtyDegrees']={'hp':hp,'state':state(t)}
        wait_for(t,lambda q:q['part']==5 and q['shownSourceFrame']>=8)
        report['oracles'].append(oracle(t,m,source,folder,'boss-departure'))
        assert state(t)['hp']==hp
        objects=list(struct.iter_unpack('>8h',read(t,0xef000,112)))
        assert not any(o[3]==1 for o in objects)
        report['checks']['bossNoNormalEnemiesArrivalHoldDeparture']=True
        wait_for(t,lambda q:q['part']==0)
        report['oracles'].append(oracle(t,m,source,folder,'stars-return'))
        report['checks']['naturalDeadlineDepartureNoPhaseInjection']=True
        # A second playlist reuses the same frame payload; no frame/time injection.
        set_keys(t,64);advance(t,1);set_keys(t,32);advance(t,1);set_keys(t,0)
        wait_for(t,lambda q:q['part']==1 and q['shownSourceFrame']>=72)
        report['oracles'].append(oracle(t,m,source,folder,'asteroids'))
        report['checks']['secondPlaylistReusesAssets']=True
        report['complete']=True
    except Exception as e:
        report['failure']=repr(e);raise
    finally:
        report['finalState']=state(t);save(folder/'result.json',report)
        save(OUT/'latest-verification.json',{'report':str(folder/'result.json')})
        print(json.dumps({'report':str(folder/'result.json'),'complete':report['complete']},ensure_ascii=True),flush=True)
        client.close()

if __name__=='__main__':main()
