"""Focused local development controller. No additional Champon8 process.

It relays held keys to the prototype-owned mailbox, and displays actual guest
frame captures. This is intentionally NOT claimed as native keyboard emulation.
"""
import base64, json, os, signal, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from runtime import HERE, OUT, target, read, set_keys, state, save, stamp

def main():
    client,t,identity=target()
    expected=(OUT/'guest.bin').read_bytes()[:64]
    if read(t,0x1000,64)!=expected: raise RuntimeError('prototype_code_pin_mismatch')
    lock=threading.RLock();log=[]
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def reply(self,data,kind='application/json',code=200):
            self.send_response(code);self.send_header('Content-Type',kind);self.send_header('Cache-Control','no-store');self.end_headers();self.wfile.write(data)
        def do_GET(self):
            try:
                if self.path=='/':self.reply((HERE/'controller.html').read_bytes(),'text/html; charset=utf-8')
                elif self.path=='/status':
                    with lock:data=state(t)
                    self.reply(json.dumps(data).encode())
                elif self.path.startswith('/frame.png'):
                    with lock:r=t.observe(capture_frame=True)
                    self.reply(base64.b64decode(r['framePng']),'image/png')
                else:self.reply(b'not found','text/plain',404)
            except Exception as e:self.reply(str(e).encode(),'text/plain',503)
        def do_POST(self):
            try:
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
                    receipt=set_keys(t,mask);t.request({'op':'resume'})
                    log.append({**receipt,'state':state(t)})
                    save(OUT/'controller-input-log.json',log[-1000:])
                self.reply(b'{"ok":true}')
            except Exception as e:self.reply(str(e).encode(),'text/plain',400)
    server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
    save(OUT/'controller.json',{'at':stamp(),'pid':os.getpid(),'url':f'http://127.0.0.1:{server.server_port}',
        'identity':identity,'classification':'Owned prototype software input bridge; not physical X68000 keyboard'})
    t.request({'op':'resume'})
    try:server.serve_forever(poll_interval=.25)
    finally:
        # Never stop the host or peer tabs. Releasing this mailbox is scoped.
        with lock:set_keys(t,0)
        server.server_close();client.close()

if __name__=='__main__':main()
