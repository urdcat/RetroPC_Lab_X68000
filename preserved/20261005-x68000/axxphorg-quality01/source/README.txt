AxxPhorg X68000 rich — quality repair 2026-10-05

This independent owner prototype references fixed-projection source v0.5.
The code renders colored player/enemies/parts at runtime on MC68000. No browser
3D renderer and no offline foreground pose images are used.

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

guest.m68 is the owner wrapper; quality.m68 contains named renderer/compositor
overrides. build.py applies them before assembly and pins all source assets.
Old v0.4 artifacts are preserved in build/quality-before-20261005.
Latest quality report: QUALITY-20261005-01.json. Verification and browser-control
evidence remain separate; full game performance and user acceptance are pending.
Record real UI-operated guest frames: capture_quality_ui.py LABEL [--frames N].
Finite pipeline timing: profile_quality.py (restores prior own running state).
Write owner report only: quality_report.py (no central registry or messages).

Reuse only currently verified shared PID/EXE/workspace and immutable own instance.
No private Champon8 startup, peer-tab/layout mutation, registry edits or publishing.
