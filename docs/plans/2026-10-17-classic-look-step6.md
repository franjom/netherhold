# Step 6 — HUD and controls

Status: live minimap tiles, markers and reporting outlines (including Guard Rooms) built; 6.1 remains partial, 2026-10-18.
The native frame with its idle corner artwork is present; remaining overlays,
navigation controls and owner comparison remain open;
ADR-0497 remains proposed. Step5's outstanding visual/performance
gates remain in [its checklist](2026-10-14-classic-look-step5.md).

## Design reference

Use [ART_DIRECTION.md §13](../ART_DIRECTION.md#13-the-interface) and ADR-0497:
one scalable HUD for both 3D looks, preserving DK2's interface design. Do not
introduce a separate pixel-faithful HUD mode or a new layout. Sizing, anchors,
fonts, asset tiers, minimap rendering and acceptance rules have their home there.

The implementation order is **minimap, top bar, panel**. Keep what the HUD shows
in `DK2.Presentation`, tested headlessly; Unity draws it and forwards input to
existing command/camera interfaces. No gameplay rules move into UI callbacks.

## 6.0 — Research and rendering pilot

- [ ] Research whole-HUD and individual-panel visibility during intros, camera
  sequences and other modes, including restoration and input gating (Q610).
- [ ] Inventory install-supplied interface bitmaps, font sizes, widget states and
  existing asset-provider support. Keep original/derived artwork out of Git.
- [ ] Read the remaining minimap draw and interaction paths before implementing
  markers, zoom, click navigation or the camera footprint. Start with the existing
  function record for `0x0041CC50`; its trap arm alone is not a complete reading.
- [ ] Read the relevant layout/input paths and resolve Q197's minimap visibility
  question. `0x00427BA0` currently documents indicator packing, not the whole HUD.
- [x] Pilot UI Toolkit's custom minimap shader integration and `.bf4` font quality
  at reference and large sizes. Record results before selecting production UI
  plumbing; an alternative toolkit requires the ADR-0497 amendment it describes.

Rendering feasibility passed in an isolated URP project at both reference and
1440p sizes; [pilot checks, captures and limits](../../tools/hud-probe/README.md).
The native raster/overlay reading is in [minimap-rendering.md](../research/minimap-rendering.md).
Interactions, Q197, complete widget/art mapping and production font metrics remain
research gates; the shader pilot does not close the whole milestone.

Exit: a small reviewed design/implementation plan grounded in the binary and
shipped data, plus a working rendering feasibility result. No guessed interactions.
Check the records before decompiling and record new function findings in the KB.

## 6.1 — Minimap

The native frame with idle corner art, persisted-fog tile classifier, changed-cell GPU scatter and live
DungeonRunner disc are built. WORD classes survive the GPU path; idle frames
upload no cells. Capture runs before worker dispatch; drawing reads GPU state.
Creature/trap dots and oriented door strokes use researched fog, hand and blink
rules, with a retained marker buffer and display-resolution shapes. Pending
pickups use a documented command-settlement approximation. The static Heart and
Guard Post reporting-distance outlines, Guard Room rectangle, possession ring and
Alarm ring are built; other overlays and navigation controls remain. Whole-HUD
visibility is Q610, with the current reading in [hud-visibility.md](../research/hud-visibility.md).
Disc/frame layout and rotation still need owner comparison. Static native menu
and corner-art mapping is recorded; physical input channels and complete corner
panel behaviours remain research gates.

- [x] Present changed tile classes through a renderer-independent view; draw the
  palette, rotated map, mask and background on the GPU as specified in §13.
- [ ] Add only researched markers, blinking, zoom, camera footprint and input.
  Preserve discovery rules; hidden game state must not leak through the HUD.
- [ ] Compare beside DK2 at the same location and obtain owner acceptance.

## 6.2 — Top bar

- [ ] Present keeper resources and researched hover/status information from the
  settled view. Reuse the font and scalable frame/fill approach proved by the pilot.
- [ ] Match DK2's anchors, content, states and timing; obtain owner acceptance.

## 6.3 — Bottom panel and keeper controls

- [ ] Build the researched tabs, button states, room choices, costs, availability
  and selected-tool feedback. Room selection must be usable by name/icon without
  knowing room IDs or opening the Inspector.
- [ ] Route construction/sell/Hand actions through existing simulation commands
  and rules. Add other panels against supported capabilities; record missing
  producers rather than manufacture successful-looking actions.
- [ ] Prevent HUD clicks/drags/scrolls from also issuing world actions. Handle
  pointer capture, cancellation, focus loss and camera controls consistently.
- [ ] Review debug key bindings against [controls research](../research/controls.md)
  and the binary before presenting them as original DK2 controls. Keep diagnostics
  available without making them the player interface.

## Acceptance and boundaries

For each delivered slice: independent review, relevant headless presentation/input
tests, adapter build, and owner comparison at 1024×768 and 2560×1440 or above.
Check UI scaling, widescreen anchors, readable text, overlapping tooltips and
no commands passing through the HUD. Profile UI updates and minimap transfers;
avoid full tile-grid uploads when nothing changed.

Cursor and status-flower 2D work follows §13 after the core slices; their unknown
3D/scale behaviour is researched before implementation. The front-end 3D scene,
audio, new game mechanics and replacement-art production are separate work.
Performance P0 remains [its own roadmap](2026-10-17-parallelism-design.md), not a
new requirement to parallelize the HUD before measuring it.
