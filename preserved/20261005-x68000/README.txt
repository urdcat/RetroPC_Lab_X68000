X68000 separate preservation checkpoint — 2026-10-05

These are copies, not a rename, replacement, or deletion of original games.
Run Preserve.ps1 from PowerShell to verify all original/copy hashes. Existing
different snapshot files are never overwritten. INVENTORY.json records every
payload and exact external dependency; missing dependencies are explicit.

human68k-samples: old HELLO / BREAKOUT source and locally retained .X binaries.
Build at the canonical original src/platform/x68k using its Makefile and xdev68k;
launch .X through Human68k/XEiJ separately. No hardware re-verification here.

legacy-polygon: Star Cruiser model rotation native512/SSAA, original procedural
local-cache mini-game, auto-cycle/boss versions, and v11 player-speed game.
Source wrappers and builders are preserved. The builders are frozen historical
code: run from the original workspace and retain its src/tools relative layout.
Rotation entry: tools/build_champon8_polygon_demo.py or SSAA builder; native-fast
entry: benchmark_champon8_native_fast.py. Local procedural game entry starts at
tools/build_champon8_local_cached.py; subsequent build_* scripts form v05–v11.
All use the pinned external m68kasm. Do not auto-launch/deploy old closed tabs.
Their RAM-entry guest BINs are retained under build/; v11 candidate is the old
game, never AxxPhorg acceptance. AxxPhorg has its own independent prototype.

Star Cruiser reference geometry originated from a commercial Mega Drive ROM.
The ROM is NOT copied or published. Derived geometry/AA tables/rotation BINs
are retained only under ignored build/; hashes/references are in INVENTORY.json.
Author-written renderer source is separate from those restricted data assets.
Original procedural local-cache meshes do not use that commercial geometry.

axxphorg-first-rich-v04 and axxphorg-quality01: independently frozen source,
BIN/background/build metadata and saved verification evidence. Their original
archives remain in prototypes/axxphorg-rich/build/quality-before-20261005*.
Build from the matching archived source with the exact manifest dependency;
current prototype changes do not modify or retroactively validate these copies.
QUALITY-01 had fixed-camera/pixel/input evidence but boss 2.05 pageHz at nominal
10MHz; low input response and game/quality acceptance were NOT passed.

Snapshots preserve historical evidence, not a new hardware/keyboard/game pass.
Shared Champon8 current PID/EXE/workspace and immutable owner instance must be
checked before loading any BIN. No private host or unrelated-tab mutation.

Development prototype entry: D:/work/X68000で何か作ろう/prototypes/axxphorg-rich
Collection report: PRESERVATION-PUBLICATION-20261005-01.json in this folder.
Current native-background/scheduling work is not part of this first publication.
