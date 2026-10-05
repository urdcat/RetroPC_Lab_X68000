AxxPhorg X68000 rich — QUALITY-02 checkpoint 2026-10-05

This independent owner prototype references fixed-projection source v0.5.
The code renders colored player/enemies/parts at runtime on MC68000. No browser
3D renderer and no offline foreground pose images are used. Background art is
re-rasterized offline at native512x384, with the same fixed world camera/FOV,
models, paths and colors. It is not SSAA and not proof of runtime background 3D.
The 16x16 repeat-row/color-run dictionary fits standard 1MiB with no expansion.

Build: bundled Python -B prototypes/axxphorg-rich/build.py
Verify own shared instance: bundled Python -B prototypes/axxphorg-rich/verify.py
Load normal RAM BIN: bundled Python -B prototypes/axxphorg-rich/runtime.py load
Start owner controller: bundled Python -B prototypes/axxphorg-rich/controller.py
Controls: http://127.0.0.1:56206/ — WASD/arrows, Z/Space, Enter, P.
Accessible held controls persist until unchecked. Blur/hidden/idle releases input.
Stop button ends only this helper and pauses the owned guest, never the host.

No native Champon8 X68000 keyboard acceptance is claimed. The browser is a
software mailbox adapter to the unchanged guest input ABI. It displays actual
Champon8 captures. Physical input, IPL/IOCS/Human68k and real hardware are pending.

guest.m68 is the owner wrapper; quality.m68 / native_bg.m68 contain named
renderer/compositor overrides. build.py pins external common source v0.5 plus
the published frozen renderer under preserved/20261005-x68000/legacy-polygon.
External common artwork/SDK/assembler references are required; build/ is local
generated evidence, not tracked. No ROM or credentials are published.
Old v0.4, QUALITY-01 and legacy games/rotation demos are preserved separately
under preserved/20261005-x68000 and build/quality-before-20261005[-02].
Latest quality report: QUALITY-20261005-02.json. Verification and browser-control
evidence remain separate; full game performance and user acceptance are pending.
Record real UI-operated guest frames: capture_quality_ui.py LABEL [--frames N]
[--version 02]. Natural same-clock before/after windows: compare_latency.py
[--baseline]. This records actual held -> movement -> released guest images.
Finite pipeline timing: profile_quality.py (restores prior own running state).
Write QUALITY-02 owner report: quality02_report.py (no central registry/messages).
quality_report.py is the historical QUALITY-01 writer; do not rerun over its
archived evidence with a newer binary.

QUALITY-02: mixed/solid dispatch corruption and stale high-word addresses fixed.
Only complete local images are published. Non-player poses use one bounded
phase/page (at most four whole faces) in a staging image; player translation and
shots do not wait for all six boss poses. No face/model/color simplification.
Final BIN 37926 bytes, SHA256
eca7e44515196a5a259ca7de020d818648b2003b4224391b99c959f867483c7b.
Full verification: build/verification-1791194791688474900/result.json.
Twelve scenes: every background/page pixel agrees with independent reference.
Nominal 10MHz full-page averages: stars 9.89Hz, battleship 4.47Hz, boss 2.91Hz.
QUALITY-01 was 4.15/5.66/2.05Hz respectively. Background pixel workload is now
4x; battleship is slower, so this is not a uniform performance win. Boss input
latency and low pose/background rates remain unresolved. Background source cap
is 3Hz. Per-pixel foreground/background depth masking and full game flow absent.

Reuse only currently verified shared PID/EXE/workspace and immutable own instance.
No private Champon8 startup, peer-tab/layout mutation or registry edits.
Publication is scoped to explicitly authorized authored files and compact
reports; never include build payloads, commercial ROM, shared secrets or peers.
