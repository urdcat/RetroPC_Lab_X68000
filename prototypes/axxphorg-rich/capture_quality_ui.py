"""Record real guest frames while normal browser controls are operated.

This recorder never injects keys, edits scene state, pauses a guest, or touches
another instance. UI observations and physical-key acceptance stay separate.
"""
import argparse, base64, json
from PIL import Image
from runtime import OUT, target, read, state, save, sha, stamp


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('label')
    parser.add_argument('--frames', type=int, default=1)
    parser.add_argument('--version',choices=['01','02'],default='02')
    args = parser.parse_args()
    if not args.label.replace('-', '').isalnum() or not 1 <= args.frames <= 12:
        raise ValueError('bounded label/frames required')
    client, guest, identity = target()
    try:
        binary = (OUT / 'guest.bin').read_bytes()
        assert read(guest, 0x1000, 64) == binary[:64], 'code prefix mismatch'
        samples = []
        for index in range(args.frames):
            before = state(guest)
            frame = guest.observe(capture_frame=True)
            png = base64.b64decode(frame['framePng'])
            path = OUT / f'quality{args.version}-ui-{args.label}-{index:02d}.png'
            path.write_bytes(png)
            with Image.open(path) as image:
                colors = len(image.convert('RGB').getcolors(image.width * image.height))
                size = list(image.size)
            samples.append({'at': stamp(), 'before': before, 'after': state(guest),
                            'screenshot': str(path), 'sha256': sha(png),
                            'dimensions': size, 'distinctRgbColors': colors})
        status = guest.request({'op': 'status'})
    finally:
        client.close()
    evidence_path = OUT / ('QUALITY-UI.json' if args.version=='01' else 'QUALITY02-UI.json')
    evidence = json.loads(evidence_path.read_text('utf8')) if evidence_path.exists() else {
        'classification': 'Normal local browser controls -> own software mailbox; actual guest CPU frame captures. Not native Champon8 or physical X68000 keyboard acceptance.',
        'nativeKeyboardAccepted': False, 'physicalHardwareAccepted': False,
        'inputLog': str(OUT / 'controller-input-log.json'), 'observations': []}
    evidence.update(reportedAt=stamp(), binarySha256=sha(binary),
                    instanceId=identity['instanceId'], workspaceId=identity['workspaceId'],
                    controllerUrl='http://127.0.0.1:56206/')
    evidence['observations'].append({'label': args.label, 'running': status['running'],
                                     'samples': samples})
    save(evidence_path, evidence)
    print(json.dumps({'label': args.label, 'running': status['running'],
                      'states': [s['after'] for s in samples],
                      'screenshots': [s['screenshot'] for s in samples]}, ensure_ascii=True))


if __name__ == '__main__':
    main()
