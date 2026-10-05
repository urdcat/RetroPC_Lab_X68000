"""Bounded inventory reconciliation; never changes archived payloads or a guest.

Save byte-exact Git/local metadata snapshots and audit all 516 original/copy pairs.
Existing evidence must be identical or this script stops (no snapshot overwrite).
"""
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
COMMIT = '878c6aa2213b0295e69a4cd1f788bf09737ef905'
RELATIVE = 'preserved/20261005-x68000/INVENTORY.json'
EXPECTED_PUBLISHED = 'b6efb4a3fc505a6c0ac25c28f56f6ae7a84b767d6c5dde03f999ffbbd8fff6ea'
EXPECTED_CURRENT = 'a23075d5c8907fa32b62859a65f173617b62f0e9ec129b7f5cb79404ba0a1b99'
OLD_RECEIPT_EXPECTATION = '9b93de02031d51fa60fbf525820b9e86d435a90fa8ff6d292f125e87af115f20'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def immutable_snapshot(name, data):
    path = HERE / 'inventory-snapshots' / name
    path.parent.mkdir(exist_ok=True)
    if path.exists():
        assert path.read_bytes() == data, ('existing_evidence_differs', str(path))
    else:
        path.write_bytes(data)
    return {'path': str(path), 'bytes': len(data), 'sha256': digest(data)}


def main():
    published_bytes = subprocess.check_output(['git', 'cat-file', 'blob', f'{COMMIT}:{RELATIVE}'], cwd=REPO)
    current_bytes = (HERE / 'INVENTORY.json').read_bytes()
    assert digest(published_bytes) == EXPECTED_PUBLISHED
    assert digest(current_bytes) == EXPECTED_CURRENT
    published = json.loads(published_bytes)
    current = json.loads(current_bytes)
    # Payload records must be identical, not merely equal in count or digest.
    assert published['files'] == current['files'] and len(current['files']) == 516
    changed_keys = [k for k in sorted(set(published) | set(current)) if published.get(k) != current.get(k)]
    assert changed_keys == ['externalDependencies'], changed_keys
    pairs = []
    for entry in current['files']:
        original, copy = Path(entry['original']), Path(entry['preserved'])
        assert copy.resolve().is_relative_to(HERE)
        assert original.resolve().is_relative_to(REPO)
        assert original.stat().st_size == copy.stat().st_size == entry['bytes']
        original_hash, copy_hash = digest(original.read_bytes()), digest(copy.read_bytes())
        assert original_hash == copy_hash == entry['sha256'], entry['relative']
        pairs.append({'relative': entry['relative'], 'sha256': copy_hash})
    snapshots = [immutable_snapshot('published-878c6aa-inventory.json', published_bytes),
                 immutable_snapshot('dependency-corrected-a23075-inventory.json', current_bytes)]
    old_ref, new_ref = published['externalDependencies'][7], current['externalDependencies'][7]
    report = {
        'reportId': '20261005-x68000-preservation-publication-inventory-supplement-02',
        'reportedAt': datetime.now(timezone.utc).isoformat(),
        'scope': 'Metadata reconciliation and file hashing only; no emulator access, payload mutation, or all-game re-test',
        'publication': {'commit': COMMIT, 'successfulPushRecordUnchanged': True,
                        'publishedGitBlobSha256': EXPECTED_PUBLISHED},
        'originalReceiptExpectation': {
            'sha256': OLD_RECEIPT_EXPECTATION,
            'matchesPublishedGitBlob': False, 'matchesCurrentInventory': False,
            'classification': 'Stale unmatched local intermediate expectation; exact historical bytes not recovered. Not a payload corruption diagnosis.'},
        'currentInventory': {'sha256': EXPECTED_CURRENT, 'bytes': len(current_bytes),
                             'classification': 'Post-publication external-dependency correction'},
        'reason': 'model_reference.py PORT was inspected after publication. Its actual existing ROM reference is under tmp/starcruiser-render-opt-20260905/ports/star-cruiser. The previous guessed path was missing. Three xdev68k tool references were also added for old Human68k samples.',
        'differences': {'topLevelKeys': changed_keys, 'externalDependenciesBefore': 8,
                        'externalDependenciesAfter': 11, 'oldRestrictedReference': old_ref,
                        'correctedRestrictedReference': new_ref,
                        'addedDependencies': current['externalDependencies'][8:]},
        'payload': {'recordsUnchanged': True, 'entries': len(pairs),
                    'originalAndCopyPairsRehashed': len(pairs), 'allHashesAndSizesMatch': True,
                    'removedOverwrittenOrMoved': False,
                    'orderedRecordDigestSha256': digest(json.dumps(pairs, sort_keys=True, ensure_ascii=False).encode('utf8'))},
        'byteExactSnapshots': snapshots,
        'independentCoordinatorEvidence': 'D:/work/PolygonGames/proposals/20261005-axxphorg-preservation-publication-01/X68000-INVENTORY-COMPARISON.json',
        'restrictedMaterialAcquiredCopiedOrPublished': False,
        'resolution': 'Published and current inventory bytes are now separately identified. Only dependency metadata differs. Preserve the unmatched 9b93 expectation in the original receipt; do not silently relabel it as a verified snapshot.'}
    output = HERE / 'PRESERVATION-PUBLICATION-INVENTORY-SUPPLEMENT-20261005-02.json'
    if output.exists():
        raise RuntimeError('supplement_already_exists; preserve existing evidence')
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    print(json.dumps({'report': str(output), 'payloadPairsVerified': len(pairs),
                      'publishedSha256': EXPECTED_PUBLISHED, 'currentSha256': EXPECTED_CURRENT}, ensure_ascii=True))


if __name__ == '__main__':
    main()
