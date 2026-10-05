"""Finite owned-instance pose sub-stage cycle measurement, no RAM injection."""
import json,time
from runtime import target,state,OUT,save,read,stamp

def main():
    m=json.loads((OUT/'build.json').read_text('utf8'));s=m['symbols']
    c,t,identity=target();running=t.request({'op':'status'})['running'];t.request({'op':'pause'})
    def until(label):
        cycles=0
        for _ in range(80):
            r=t.request({'op':'run-until','pc':s[label],'maxCycles':2000000,'maxInstructions':100000})
            cycles+=r['executedCycles']
            if r.get('conditionMatched'):return cycles
            if r['stopReason'] not in ('cycle-limit','instruction-limit'):raise RuntimeError(r['stopReason'])
        raise RuntimeError('finite_pose_budget')
    try:
        until('render_one');start=state(t);buckets=[]
        for label in ['transform_vertices','build_face_order','sort_face_order','draw_faces','record_image_bounds','render_one.done']:
            cycles=until(label);buckets.append({'until':label,'cycles':cycles,'nominalMs':cycles/10000})
        scalar=lambda n:int.from_bytes(read(t,s[n],2),'big',signed=True)
        report={'at':stamp(),'binarySha256':m['binarySha256'],'identity':identity,
            'before':start,'after':state(t),'buckets':buckets,
            'renderSlot':scalar('render_slot'),'size':scalar('render_size'),
            'depth':scalar('pose_depth'),'vertices':scalar('render_vertex_count'),
            'faces':scalar('render_face_count'),'classification':'nominal guest cycles, not physical hardware'}
        p=OUT/('pose-profile-'+str(time.time_ns())+'.json');save(p,report)
        print(json.dumps({k:v for k,v in report.items() if k!='identity'}),flush=True)
    finally:
        if running:t.request({'op':'resume'})
        c.close()

if __name__=='__main__':main()
