"""Separate immutable QUALITY-02 source/artifact checkpoint; no ROM or secrets.

Mechanical copies only, with byte/hash verification and no existing overwrite.
Generated binaries/reports stay in ignored build/. Original demos are untouched.
"""
import json
import shutil
from pathlib import Path
from runtime import HERE, OUT, sha

ROOT = HERE.parents[1] / 'preserved/20261005-x68000/axxphorg-quality02'


def main():
    report = json.loads((HERE / 'QUALITY-20261005-02.json').read_text('utf8'))
    m = json.loads((OUT / 'build.json').read_text('utf8'))
    assert report['build']['artifacts'][0]['sha256'] == m['binarySha256']
    sources = report['source']['entryPoints'] + ['QUALITY-20261005-02.json']
    files = [(HERE / name, 'source/' + name) for name in sources]
    files += [(OUT / name, 'build/artifact-' + name) for name in ['guest.bin','guest.m68','background.bin','build.json']]
    files += [(Path(report['verification']['report']), 'build/verification-result.json'),
              (Path(report['sameClockInputComparison']['before']['path']), 'build/before-input-result.json'),
              (Path(report['sameClockInputComparison']['after']['path']), 'build/after-input-result.json'),
              (OUT / 'QUALITY02-UI.json', 'build/UI-result.json')]
    for i, item in enumerate(report['performance']['pipelineProfiles']):
        files.append((Path(item['reference']), f'build/pipeline-{i:02d}.json'))
    entries = []
    for source, relative in files:
        destination = ROOT / relative
        assert source.resolve().is_relative_to(HERE)
        assert destination.resolve().is_relative_to(ROOT)
        data = source.read_bytes()
        if destination.exists():
            assert destination.read_bytes() == data, ('existing_snapshot_differs', relative)
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        assert sha(destination.read_bytes()) == sha(data)
        entries.append({'original': str(source), 'preserved': relative,
                        'bytes': len(data), 'sha256': sha(data),
                        'publication': 'authored-source-or-compact-report' if relative.startswith('source/') else 'local-ignored-generated-artifact'})
    manifest = {'reportId':'20261005-x68000-axxphorg-quality02-snapshot',
        'binarySha256': m['binarySha256'], 'backgroundSha256':m['backgroundSha256'],
        'files':entries, 'originalsUnmodified': True,
        'entryPoint':'prototypes/axxphorg-rich/README.txt; build from the canonical project layout using published frozen legacy renderer and pinned external common artwork',
        'classification':'Quality and input-latency repair checkpoint; NOT playable/full-game/native-input/hardware acceptance',
        'notIncluded':'commercial ROM, shared credentials, large oracle cache or video; original 516-file inventory remains separately frozen'}
    data = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf8')
    destination = ROOT / 'MANIFEST.json'
    if destination.exists():
        assert destination.read_bytes() == data, 'existing_manifest_differs'
    else:
        destination.write_bytes(data)
    print(json.dumps({'snapshot':str(ROOT),'files':len(entries),'manifestSha256':sha(data)},ensure_ascii=True))


if __name__ == '__main__':
    main()
