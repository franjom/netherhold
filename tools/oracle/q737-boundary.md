# Q737 boulder-stun experiment

Build the existing `M7Q513a` recipe using [levelgen](../levelgen/README.md).
Keep that played recipe unchanged. Use an isolated copy of the native runtime
and its `Data` and `DK2TextureCache` directories so the regular install's map
corpus stays unchanged.
Copy the six generated map files into that runtime's `Data/editor/maps`.
Verify copied inputs with SHA-256 and retain the copy until cleanup is approved.

Before launching, warn the owner that debugger pauses can begin during startup,
before the Heart opening animation finishes, and full memory reads can make
the visible game appear hung. From a 64-bit Windows CPython shell, with no
DK2 process already running:

```powershell
python tools/oracle/decision_boundary.py q737 --exe C:/path/to/isolated/DKII-DX.exe --out F:/DK2Evidence/studies/new-study/run-001
```

The recorder launches `-LEVEL M7Q513a` without player inputs or seed injection.
It selects a level-1 keeper-3 Troll in state 256 with a live Boulder threat,
then captures four consecutive calls to its native update, before and after
each call. It uses the documented [boundary debugger](decision-boundary.md),
hardware execution breakpoints, full readable memory and thread contexts.
The game is stopped while each memory image is read and can appear
unresponsive during those reads; capture elapsed time is not native timing.
It changes no game state or executable bytes and terminates only its own child.
Each output directory must be new; failed attempts remain evidence.

`boundaries.jsonl` includes the loaded stun interval, entry stamp, unsigned
elapsed time, fear inputs, state and health. Full memory permits inspecting
the original Boulder even if the Troll clears its threat field. Compare each
after-state with the next before-state to distinguish the creature update
from intervening object processing. `verified.json` exists only after all
requested pairs, unchanged level files and successful child shutdown.

This fresh run can establish a mechanism; it does not reproduce the unknown
seed or full initial state of the legacy capture. Findings belong in
[boulder §9c](../../docs/research/boulder.md#9c-q737-recheck-stun-duration-does-not-establish-the-next-state).

Synthetic recorder and debugger checks:

```powershell
python -m unittest discover -s tools/oracle -p test_decision_boundary.py -v
```
