"""Finite pipeline cycle buckets on the owned paused instance, no RAM injection."""
import json,time
from runtime import OUT,target,state,save,stamp

def main():
    m=json.loads((OUT/'build.json').read_text('utf8'));s=m['symbols']
    c,t,identity=target();was_running=t.request({'op':'status'})['running'];t.request({'op':'pause'})
    def until(label):
        cycles=0
        for n in range(40):
            r=t.request({'op':'run-until','maxCycles':2000000,'maxInstructions':100000,'pc':s[label]})
            cycles+=r['executedCycles']
            if r.get('conditionMatched'):return cycles
            if r['stopReason'] not in ('instruction-limit','cycle-limit'):raise RuntimeError(r)
        raise RuntimeError('bounded_profile_limit')
    try:
        until('entry.main');start=state(t)
        labels=['service','sequence_service','background_service','object_snapshot','pose_jobs','sync_object_damage','restore_damage','order_objects','draw_objects','draw_shots','entry.blank','entry.main']
        buckets=[]
        for label in labels:buckets.append({'until':label,'cycles':until(label)})
        report={'at':stamp(),'identity':identity,'binarySha256':m['binarySha256'],'before':start,'after':state(t),'buckets':buckets,
                'classification':'pipeline buckets include nested input/clock service; background restore includes late-player snapshot/damage/second restore; nominal cycles, not physical hardware timing'}
        names=['main_call','input_clock_service','sequence','background_dictionary','object_snapshot',
               'changed_local_images','per_page_damage','background_restore','foreground_sort','foreground_blit','shots','vblank_flip']
        for bucket,name in zip(buckets,names):
            bucket.update(stage=name,nominalMilliseconds=bucket['cycles']/10000)
        report['totalCycles']=sum(b['cycles'] for b in buckets)
        report['nominalMilliseconds']=report['totalCycles']/10000
        p=OUT/('quality-profile-'+str(time.time_ns())+'.json');save(p,report)
        pointer_path=OUT/'latest-quality-profile.json'
        prior=json.loads(pointer_path.read_text('utf8')) if pointer_path.exists() else {}
        reports=prior.get('reports',[prior['report']]) if prior.get('binarySha256')==m['binarySha256'] else []
        save(pointer_path,{'report':str(p),'reports':(reports+[str(p)])[-10:],'binarySha256':m['binarySha256']})
        print(json.dumps({k:v for k,v in report.items() if k!='identity'},ensure_ascii=True));print(p)
    finally:
        if was_running:t.request({'op':'resume'})
        c.close()

if __name__=='__main__':main()
