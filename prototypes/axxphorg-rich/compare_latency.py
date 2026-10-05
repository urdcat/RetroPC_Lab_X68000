"""Same-clock input windows, actual continuous frames, only the owned guest.

No scene, time, camera, models or HP are injected. Both versions boot normally
and reach each scene through the regular input ABI and natural sequence.
Input is software mailbox input, never native-key acceptance.
"""
import argparse, base64, json, struct, time
from pathlib import Path
from PIL import Image
from runtime import OUT, target, read, state, load, advance, set_keys, save, stamp, sha


def run_slice(t, address=None, expected=None):
    command={'op':'run-until','maxCycles':100000,'maxInstructions':10000}
    if address is not None:
        command['memory']={'space':'cpu-logical','address':address,'hex':expected.hex()}
    result=t.request(command)
    if not result.get('conditionMatched') and result['stopReason'] not in ('instruction-limit','cycle-limit'):
        raise RuntimeError(('unexpected_guest_stop',result['stopReason']))
    return result


def until_memory(t, address, expected, limit=20000):
    for _ in range(limit):
        if read(t,address,len(expected))==expected:return
        if run_slice(t,address,expected).get('conditionMatched'):return
    raise RuntimeError('finite_normal_wait_budget')


def image_sample(t, folder, number, cycles):
    st=state(t)
    page=int.from_bytes(read(t,0xe82600,2),'big')
    assert page in (1,2)
    display=list(struct.unpack('>12h',read(t,0xb3000+(168 if page==2 else 0),24)))
    png=base64.b64decode(t.observe(capture_frame=True)['framePng'])
    path=folder/f'frame-{number:04d}.png';path.write_bytes(png)
    with Image.open(path) as im:
        assert im.size==(512,512)
        colors=len(im.convert('RGB').getcolors(262144))
    return {'nominalSeconds':cycles/10000000,'state':st,'displayedHeroCenter':display[:2],
            'displayedHeroValid':bool(display[5]),'screenshot':str(path),'imageSha256':sha(png),
            'distinctColors':colors}


def window(t, folder, name, fire):
    folder.mkdir();samples=[];events=[];cycles=0;wall=time.perf_counter()
    for label,mask in [('neutral',0),('right',18 if fire else 2),('release-right',0),
                       ('left',1),('release-left',0)]:
        start=state(t);end_tick=start['tick']+32
        receipt=set_keys(t,mask)
        event={'label':label,'mask':mask,'atNominalSeconds':cycles/10000000,
               'beginTick':start['tick'],'deadlineTick':end_tick,'receipt':receipt}
        events.append(event);samples.append(image_sample(t,folder,len(samples),cycles))
        last_page=samples[-1]['state']['pages']
        for _ in range(3000):
            result=run_slice(t,0xf0004,end_tick.to_bytes(4,'big'))
            cycles+=result['executedCycles'];st=state(t)
            if st['pages']!=last_page or result.get('conditionMatched'):
                samples.append(image_sample(t,folder,len(samples),cycles));last_page=st['pages']
            if result.get('conditionMatched'):break
        else:raise RuntimeError(('input_window_budget',name,label))
        event['endState']=state(t)
        print(json.dumps({'window':name,'input':label,'pages':last_page,'tick':st['tick']},ensure_ascii=True),flush=True)
    set_keys(t,0)
    # Finish two complete pages after release: verify actual visible position
    # settles, rather than interpreting the current player variable as display.
    for _ in range(3):
        a=advance(t,1);cycles+=a['executedCycles']
        samples.append(image_sample(t,folder,len(samples),cycles))
    assert len({tuple(s['displayedHeroCenter']) for s in samples[-3:]})==1,('visible_release_did_not_settle',name)
    assert all(s['state']['keys']==0 for s in samples[-3:])
    images=[];durations=[]
    for index,sample in enumerate(samples):
        with Image.open(sample['screenshot']) as im:images.append(im.convert('RGB'))
        next_time=samples[index+1]['nominalSeconds'] if index+1<len(samples) else sample['nominalSeconds']+.15
        durations.append(max(10,round((next_time-sample['nominalSeconds'])*1000/10)*10))
    gif=folder/'continuous.gif'
    images[0].save(gif,save_all=True,append_images=images[1:],duration=durations,loop=0,disposal=2)
    for im in images:im.close()
    latency={}
    for event in events:
        if event['label'] not in ('right','left'):continue
        start_sample=max((s for s in samples if s['nominalSeconds']<=event['atNominalSeconds']),key=lambda s:s['nominalSeconds'])
        direction=1 if event['label']=='right' else -1
        first=next((s for s in samples if s['nominalSeconds']>event['atNominalSeconds'] and
                    (s['displayedHeroCenter'][0]-start_sample['displayedHeroCenter'][0])*direction>2),None)
        assert first,('no_visible_motion',name,event['label'])
        latency[event['label']]=(first['nominalSeconds']-event['atNominalSeconds'])*1000
    report={'name':name,'classification':'normal software mailbox input; actual guest frames; GIF duration based on nominal executed cycles, not wall-time or hardware FPS',
            'events':events,'samples':samples,'continuousRecording':str(gif),
            'nominalGuestSeconds':cycles/10000000,'wallSeconds':time.perf_counter()-wall,
            'firstVisibleMovementMilliseconds':latency,'releaseVisibleSettles':True}
    save(folder/'result.json',report)
    return report


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--baseline',action='store_true');args=parser.parse_args()
    archive=OUT/'quality-before-20261005-02'
    metadata=archive/'artifact-build.json' if args.baseline else OUT/'build.json'
    binary=archive/'artifact-guest.bin' if args.baseline else OUT/'guest.bin'
    background=archive/'artifact-background.bin' if args.baseline else OUT/'background.bin'
    m=json.loads(metadata.read_text('utf8'))
    assert sha(binary.read_bytes())==m['binarySha256']
    name='before' if args.baseline else 'after'
    folder=OUT/('quality02-'+name+'-'+str(time.time_ns()));folder.mkdir()
    client,t,identity=target()
    report={'at':stamp(),'version':name,'binarySha256':m['binarySha256'],
            'backgroundSha256':sha(background.read_bytes()),'sourceManifestSha256':m['sourceManifestSha256'],
            'identity':identity,'windows':[],'complete':False,
            'workload':{'playerStart':[0,-30],'heldTicks':32,'releasedTicks':32,'starsStartAbsoluteTick':512,
                        'otherScenesStartElapsedTick':224,'phaseTimeCameraHpInjection':False,'nativeKeyboard':False}}
    try:
        load(t,binary,background);advance(t,1)
        until_memory(t,0xf0004,(512).to_bytes(4,'big'))
        report['windows'].append(window(t,folder/'stars','stars',True))
        # Request a regular scene change, then allow the natural stars boundary.
        set_keys(t,32);tick=state(t)['tick']
        until_memory(t,0xf0004,(tick+2).to_bytes(4,'big'));set_keys(t,0)
        until_memory(t,0xf000c,(2).to_bytes(2,'big'))
        until_memory(t,m['symbols']['part_tick'],(224).to_bytes(2,'big'))
        report['windows'].append(window(t,folder/'battleship','battleship',True))
        until_memory(t,0xf000c,(4).to_bytes(2,'big'))
        until_memory(t,m['symbols']['part_tick'],(224).to_bytes(2,'big'))
        report['windows'].append(window(t,folder/'boss','boss',False))
        report['complete']=True
    except Exception as error:
        report['failure']=repr(error);raise
    finally:
        report['finalState']=state(t);save(folder/'result.json',report)
        save(OUT/('latest-quality02-'+name+'.json'),{'report':str(folder/'result.json')})
        client.close()
        print(json.dumps({'report':str(folder/'result.json'),'complete':report['complete']},ensure_ascii=True),flush=True)


if __name__=='__main__':main()
