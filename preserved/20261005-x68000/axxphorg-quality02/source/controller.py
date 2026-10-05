"""Focused local development controller. No additional Champon8 process.

It relays held keys to the prototype-owned mailbox, and displays actual guest
frame captures. This is intentionally NOT claimed as native keyboard emulation.
"""
import argparse, base64, json, os, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from runtime import HERE, OUT, target, read, set_keys, state, save, stamp

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--port',type=int,default=56206);args=parser.parse_args()
    client,t,identity=target()
    expected=(OUT/'guest.bin').read_bytes()[:64]
    if read(t,0x1000,64)!=expected: raise RuntimeError('prototype_code_pin_mismatch')
    lock=threading.RLock();log=[];last_mask=0;last_seen=time.monotonic();closing=threading.Event()
    def reconnect():
        nonlocal client,t,identity
        client.close();client,t,new_identity=target()
        if (new_identity['workspaceId'],new_identity['instanceId'])!=(identity['workspaceId'],identity['instanceId']):
            raise RuntimeError('controller_instance_changed')
        identity=new_identity
        if read(t,0x1000,64)!=expected:raise RuntimeError('prototype_code_pin_mismatch')
    def request(action):
        try:return action()
        except Exception as e:
            if 'request_channel_expired' not in str(e):raise
            reconnect();return action()
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def reply(self,data,kind='application/json',code=200):
            self.send_response(code);self.send_header('Content-Type',kind);self.send_header('Cache-Control','no-store');self.end_headers();self.wfile.write(data)
        def do_GET(self):
            nonlocal last_seen
            try:
                last_seen=time.monotonic()
                if self.path=='/':self.reply((HERE/'controller.html').read_bytes(),'text/html; charset=utf-8')
                elif self.path=='/status':
                    with lock:data=request(lambda:state(t))
                    self.reply(json.dumps(data).encode())
                elif self.path.startswith('/frame.png'):
                    with lock:r=request(lambda:t.observe(capture_frame=True))
                    self.reply(base64.b64decode(r['framePng']),'image/png')
                else:self.reply(b'not found','text/plain',404)
            except Exception as e:self.reply(str(e).encode(),'text/plain',503)
        def do_POST(self):
            nonlocal last_mask,last_seen
            try:
                last_seen=time.monotonic()
                origin=f'http://127.0.0.1:{self.server.server_port}'
                if self.path not in ('/input','/stop') or self.headers.get('Origin')!=origin:raise ValueError('same_origin_input_required')
                if self.path=='/stop':
                    self.reply(b'{"ok":true}')
                    threading.Thread(target=self.server.shutdown,daemon=True).start();return
                size=int(self.headers.get('Content-Length','0'))
                if not 1<=size<=128:raise ValueError('input_size')
                payload=json.loads(self.rfile.read(size));mask=payload['mask']
                if type(mask) is not int:raise ValueError('integer_mask_required')
                with lock:
                    receipt=request(lambda:set_keys(t,mask));t.request({'op':'resume'});last_mask=mask
                    log.append({**receipt,'state':state(t)})
                    save(OUT/'controller-input-log.json',log[-1000:])
                self.reply(b'{"ok":true}')
            except Exception as e:self.reply(str(e).encode(),'text/plain',400)
    server=ThreadingHTTPServer(('127.0.0.1',args.port),Handler)
    save(OUT/'controller.json',{'at':stamp(),'pid':os.getpid(),'url':f'http://127.0.0.1:{server.server_port}',
        'identity':identity,'classification':'Owned prototype software input bridge; not physical X68000 keyboard'})
    t.request({'op':'resume'})
    def watchdog():
        nonlocal last_mask
        while not closing.wait(.5):
            if last_mask and time.monotonic()-last_seen>2:
                with lock:
                    if not last_mask or time.monotonic()-last_seen<=2:continue
                    request(lambda:set_keys(t,0));t.request({'op':'resume'});last_mask=0
                    log.append({'at':stamp(),'mask':0,'automaticRelease':'controller idle >2 seconds','state':state(t)})
                    save(OUT/'controller-input-log.json',log[-1000:])
    threading.Thread(target=watchdog,daemon=True).start()
    try:server.serve_forever(poll_interval=.25)
    finally:
        closing.set()
        # Never stop the host or peer tabs. Releasing this mailbox is scoped.
        with lock:request(lambda:set_keys(t,0))
        server.server_close();client.close()

if __name__=='__main__':main()
