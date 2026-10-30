# HUD comparison captures

Build the Release runtime assemblies, then run `./tools/classic-unity-probe/run-hud-comparison.ps1`.
The launcher creates a unique isolated project in `build/` and a unique, add-only
directory below `D:/DK2Evidence/captures/hud-2026-10-29/`. It never opens or saves
the owner's Unity project. It copies the current adapter sources and runtime DLLs;
run after rebuilding when either changes. Unity 6000.6 and the original install
at `E:/GogGames/Dungeon Keeper 2` are required. The main checkout supplies the
ignored replacement font when the worktree lacks it.

The probe captures eight full-view conditions at both 1024×768 and 2560×1440.
It asserts actual `Screen` dimensions and composites every runtime UIDocument's
distinct PanelSettings target, in panel sorting order, over a URP world render.
Stencil-capable targets preserve circular masks. Blank HUD/world captures fail.
Each PNG has state/camera/condition JSON; a manifest records source/DLL/image hashes.
Source fingerprints include shaders. The full-map GPU regression samples actual
UI pixels at real red Heart and grey Portal tiles outside icon/creature markers,
and compares their RGB with the CPU class grid's source-palette entries. A Y
inversion fails this check before a full-map capture is accepted.
Failed runs preserve partial outputs. Reruns create new directories; no cleanup
or external overwrite is performed.

The scene is Level1's Heart courtyard, focus `(14.5,1,-28.5)`, seed1, paused
after bootstrap, fog disabled. Cage selection uses a real visible diggable tile;
flower selection uses a real visible hovered creature. A missing retained message
is supplied through the real queue as a labelled presentation fixture. Full-map
preview uses the production layout pose for the authored light-object tile
farthest from the Heart focus (recorded in JSON), applied after Draw to
avoid changing the operating-system pointer. This camera override tests
preview rendering; it does not test input-to-map projection. Level1's light-object
list contains only the Heart, so that selection does not establish distant-light
coverage. Use the checklist's captured tile and limitations when reviewing it.
The same logical pointer also supplies the real RoomIndex room's centre/type to
the existing room-marker API after Draw; the overlay is repainted. This keeps
the icon and preview fixture consistent without moving the OS pointer.
Its current10-tile clip distance is a host rendering adaptation, not native
far-plane parity.
The ordinary-HUD fixture skips the bootstrap view path and uses the existing
force-off letterbox presentation request without advancing the simulation.
An active bootstrap camera path additionally verifies the host resets its mouse
baseline and motion latch; absence of that path is reported pending.

These are remake review captures. They establish neither native visual parity
nor owner acceptance. The matching-original request and review checks are in
[`hud-comparison-checklist.md`](../../docs/research/hud-comparison-checklist.md).

After fixing a failed run, `-ScratchProject <path-to-marked-probe-project>`
reuses its imported Library and refreshes source/DLL copies. Reuse is restricted
to marked HUD probe projects inside this worktree's `build/`. Every attempt still
gets a fresh external output directory and separate Unity log; prior evidence
and failed projects remain in place.
Add `-FullMapOnly` to capture only the corrected full-map view at both sizes,
plus its two preview images, in a new external directory.
