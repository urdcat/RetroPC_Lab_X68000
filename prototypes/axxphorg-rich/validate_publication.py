"""Local-only public-file syntax/secret/snapshot gate. Never prints secret text."""
import ast
import json
import re
from pathlib import Path
from runtime import HERE, OUT, sha


def main():
    report = json.loads((HERE / 'QUALITY-20261005-02.json').read_text('utf8'))
    names = report['source']['entryPoints'] + ['freeze_quality02.py', 'validate_publication.py',
        'quality_report.py', 'QUALITY-20261005-01.json', 'QUALITY-20261005-02.json', 'baseline-initial.json']
    secret = re.compile(r'sk-[A-Za-z0-9]{20,}|gh[pousr]_[A-Za-z0-9]{25,}|Bearer [A-Za-z0-9._-]{20,}|"token"\s*:\s*"[^"\s]{12,}"')
    parsed = 0
    for name in names:
        path = HERE / name
        assert path.resolve().is_relative_to(HERE) and path.parent == HERE
        text = path.read_text('utf8')
        assert not secret.search(text), ('secret_pattern_in_public_file', name)
        if path.suffix == '.py':
            ast.parse(text, filename=name); parsed += 1
        elif path.suffix == '.json':
            json.loads(text)
    for item in report['build']['artifacts']:
        path = Path(item['path'])
        assert path.resolve().is_relative_to(OUT)
        assert sha(path.read_bytes()) == item['sha256'], 'build_artifact_drift'
    snapshot = HERE.parents[1] / 'preserved/20261005-x68000/axxphorg-quality02'
    manifest = json.loads((snapshot / 'MANIFEST.json').read_text('utf8'))
    for item in manifest['files']:
        path = snapshot / item['preserved']
        assert path.resolve().is_relative_to(snapshot)
        assert sha(path.read_bytes()) == item['sha256'], ('snapshot_hash_mismatch', item['preserved'])
    print(json.dumps({'publicFilesChecked':len(names),'pythonAstParsed':parsed,
        'snapshotFilesRehashed':len(manifest['files']),'secretPatternsAbsent':True,
        'buildArtifactHashesMatch':True,'fullGameAcceptance':False}))


if __name__ == '__main__':
    main()
