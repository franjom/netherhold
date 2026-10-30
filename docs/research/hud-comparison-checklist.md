# HUD comparison checklist

Status: 16 remake full-view captures and two preview captures prepared; matching DK2 screenshots and owner
comparison pending. No visual acceptance is claimed. The standard is
[ART_DIRECTION.md](../ART_DIRECTION.md); capture tooling and conditions are in
[HUD_COMPARISON.md](../../tools/classic-unity-probe/HUD_COMPARISON.md).

## Scene and matching-original request

Use the original Level1 Heart courtyard, centred on tile `(14,28)` with focus
`(14.5,1,-28.5)`, camera preset Dungeon, reset yaw256, no possession, expanded
bottom bar. Capture at 1024×768 and 2560×1440, recording the native tick/elapsed
time, camera yaw/zoom and whether fog is enabled. Remake seed1 is recorded for
reproduction; a matching original RNG state is not established. Do not infer
pixel parity from an unmatched creature or animation frame.

The remake probe pauses after Level1 bootstrap, disables fog for inspection and
records each actual tick, camera transform and conditions beside its PNG. It
skips the bootstrap view path and uses the existing force-off letterbox
presentation request to show the ordinary HUD without advancing the simulation.
Each slice is a full view so screen proportions remain visible. All
runtime UI Toolkit panels are composited in sorting order over the world.
The second minimap sample turns the camera a further256 angle units. The
native request must reproduce that turn and preserve zoom and focus.

For the original screenshots below, keep the OS pointer away from unrelated
buttons, avoid Options/modal pages, and retain the original image dimensions.
Put new native images in a fresh subdirectory of
`D:/DK2Evidence/captures/hud-2026-10-29/`; never overwrite the remake images or
any existing evidence. Capture requests are pending owner action.

## Capture pairs

Main remake folder: `D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/`.
The full-map row uses corrected pointer-equivalent captures from
`D:/DK2Evidence/captures/hud-2026-10-29/20261029-213216-e188ff4c/`; its room icon and
preview both use the real Heart room (hover tile14,28, RoomIndex centre15,29).
The selected comparison set remains16 full views across these two runs.
All16 full views assert actual Screen dimensions before rendering and in the
live camera callback. Tick1, seed1, Level1 map30×38; camera and state hashes are
recorded in each JSON. Each folder's `source-and-image-hashes.json` records image, source and
runtime-DLL SHA-256 values. Files have prefix
`level1-heart-courtyard-<width>x<height>-` and the suffix in the table. Each
row requires both sizes. The JSON with the same basename states exactly what
was automated, including any message fixture and preview-camera override.

| Slice / suffix | Remake captures | Matching original screenshot request | Compare | Owner verdict |
|---|---|---|---|---|
| Minimap `minimap-yaw256.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-minimap-yaw256.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-minimap-yaw256.png) | Heart courtyard, reset camera, ordinary HUD, no pointer on minimap | Disc clipping, frame/corner art, scale, fog classes, camera footprint and North | Pending |
| Minimap rotation `minimap-yaw512.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-minimap-yaw512.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-minimap-yaw512.png) | Same position/zoom, turn from yaw256 to512 | Disc rotation, North and footprint relative to the frame | Pending |
| Top bar `topbar.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-topbar.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-topbar.png) | Same view with gold/mana/Heart/resource bars visible; record actual values | Bar dimensions, digit size/alignment, fill/icon placement, readable text | Pending |
| Cursor and cage `cursor-cage.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-cursor-cage.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-cursor-cage.png) | Hover the taggable tile named in the remake JSON, using the dig cursor; record pointer hotspot | Hand scale/anchor, cage terrain deformation, colour/pulse and overlap | Pending |
| Bottom sectors `bottom-sectors.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-bottom-sectors.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-bottom-sectors.png) | Open Rooms with the bottom bar expanded; same learned availability where possible | Tab/sector widths, icon spacing, disabled states, frame and screen proportions | Pending |
| Message panel `message-panel.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-message-panel.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-message-panel.png) | Open the first retained Level1 speech message (JSON records the message reference/fixture) | Menu39 tabs, menu40 bounds, text wrapping, scroll controls, dismiss/confirm/Goto art | Pending |
| Flowers `flowers.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-1024x768-flowers.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-211058-cf8af9a3/level1-heart-courtyard-2560x1440-flowers.png) | Hover the same creature type at the JSON location; record health/level and cycle phase | Petal ring/centre, scale, world height, colour and occlusion | Pending |
| Full map `fullmap.png` | [1024](D:/DK2Evidence/captures/hud-2026-10-29/20261029-213216-e188ff4c/level1-heart-courtyard-1024x768-fullmap.png), [2560](D:/DK2Evidence/captures/hud-2026-10-29/20261029-213216-e188ff4c/level1-heart-courtyard-2560x1440-fullmap.png) | Open M from the courtyard, place pointer at tile(14,28), record ownership/exploration | Aspect/placement, map classes, markers/legend, preview rectangle and preview lighting | Pending |

## Automation limits and follow-up

The cursor/cage view reads a real visible taggable tile; it does not alter terrain.
The flower view hovers a real visible creature without changing its health or
state. The retained-message case uses an existing record, or posts level speech1
through the queue with the injection explicitly labelled. The latter exercises
the renderer and is not evidence that the original level posted it at that tick.

The full-map far preview applies the production layout's camera pose for the
authored light-object tile farthest from the Heart focus after Draw and prepares
lighting for both cameras. The corrected full-map pair also supplies the real
RoomIndex room centre/type to ClassicFullMapMarkers.TryRoom and repaints the
overlay after Draw, matching the preview's logical pointer. This is a labelled
presentation fixture, not operating-system input automation. In this set the only light-object tile is the Heart
at `(14,28)`; this is a Heart preview and **does not verify distant lighting**.
The existing `level1-far-preview-*.png` filenames are retained under the add-only
evidence rule; their same-run JSON and this checklist specify the actual view.
Its current10-tile clip distance is a host rendering
adaptation, not a claim about native far-plane parity. This is an isolated
render test; pointer-to-map projection is covered separately by the E1 tests.
Additional preview-only PNGs expose blank or unlit preview failures. Fog is off
in the remake inspection set, so a native fog-on screenshot needs a separate
comparison of disc/frame geometry rather than a terrain-colour equality claim.

The final full-map pair passes GPU world-tile/palette checks on Vulkan at both
sizes: uncovered red Heart and grey Portal samples match the CPU class grid
and palette exactly. The prior shader failed both samples; the corrected
top-origin sampling aligns terrain with room and creature overlays. Other
graphics backends and matched-original visual parity remain unverified.

The actual bootstrap path verified the host clears its mouse baseline and
motion latch. Dollar asset lookup, complete animation loading and Unity mesh
resolution also passed. These checks establish runtime behavior, not native
visual parity. A supplemental distant-lit preview remains for the final runtime
probe. Rejected earlier attempts remain preserved in separate run directories;
the initial tile(35,35) preview was outside this30×38 map.

For each pair, the owner records accepted or a concrete mismatch with the image
names. A mismatch becomes a scoped fix or an open-question row with a closure
experiment. Keep this table pending until those comparisons occur; automation
success and remake image inspection alone do not close a Step6 slice.
