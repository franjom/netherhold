# Step 6 — HUD and controls

Status: minimap, resource controls, ordinary campaign visibility and Options
pause, full-map terrain/navigation, query/follow, bottom choices and creature
overview are integrated on classic-look/step6, 2026-10-18. The overview includes
totals, jobs, combat and moods with filtered pickup/focus. All milestones remain
subject to their remaining research and owner-comparison gates below;
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

- [x] Implement confirmed camera/count-based letterbox HUD visibility and ordinary
  input gates, including nested requests and restoration before the bars finish.
- [ ] Research remaining panel, modal and full-map visibility/input producers
  and compare restoration in original DK2 (Q611).
- [ ] Inventory install-supplied interface bitmaps, font sizes, widget states and
  existing asset-provider support. Keep original/derived artwork out of Git.
- [ ] Resolve the remaining overlay producers and secondary map/corner
  controls before implementing them; use the current function records and
  [minimap research](../research/minimap-rendering.md).
- [ ] Read the relevant layout/input paths and resolve Q197's minimap visibility
  question. `0x00427BA0` lays out only the indicator menus 45-50 (built: research/message-tabs.md §9).
- [x] Pilot UI Toolkit's custom minimap shader integration and `.bf4` font quality
  at reference and large sizes. Record results before selecting production UI
  plumbing; an alternative toolkit requires the ADR-0497 amendment it describes.

Rendering feasibility passed in an isolated URP project at both reference and
1440p sizes; [pilot checks, captures and limits](../../tools/hud-probe/README.md).
The native raster/overlay reading is in [minimap-rendering.md](../research/minimap-rendering.md).
Remaining interactions, Q197, complete widget/art mapping and production font metrics remain
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
Alarm ring, textured camera footprint, magnifier zoom and primary click navigation
and heading-dependent native north marker are built. The zoomed map has the
native phased Heart direction dots. The camera ribbon is masked
to the disc; other overlays and corner controls remain. Whole-HUD
camera/letterbox visibility and input gates are built; remaining panel/modal rules
are Q611, with the current reading in [hud-visibility.md](../research/hud-visibility.md).
Disc/frame layout and rotation still need owner comparison. Static native menu
and corner-art mapping and default primary/secondary bindings are recorded;
Query/Sell/Options corner bindings, Heart/GUI/full-map hotkeys and secondary
disc entry to the full map are integrated; see
[minimap-controls.md](../research/minimap-controls.md).

- [x] Present changed tile classes through a renderer-independent view; draw the
  palette, rotated map, mask and background on the GPU as specified in §13.
- [x] Add researched creature/trap/door markers, reporting and pulse outlines,
  zoom, camera footprint and primary navigation, preserving discovery rules.
- [ ] Complete the remaining overlays and corner controls against their research.
- [ ] Compare beside DK2 at the same location and obtain owner acceptance.

## 6.2 — Top bar

- [x] Present settled gold, mana, separate incoming/outgoing rates, the Heart
  health ring and supported hover/status text using original frames and sprites.
- [x] Complete resource hover/clicks, payday countdown and wage-bill flashing,
  damage alarms and message flashing. Native evidence and remaining producers:
  [hud-resources.md](../research/hud-resources.md).
- [ ] Match DK2's anchors, content, states and timing; obtain owner acceptance.

### Cursor and world hover (owner priority, 2026-10-18)

- [x] Research and draw the blue tile-edge highlight and context-dependent Hand
  sprites, including the excavation cursor, without changing command rules.
- [x] Research eligible world tooltips and creature hover status. Draw original
  localized names and supported status fields; do not invent missing producers.
- [x] Capture hover facts before worker dispatch; rendering reads retained data.
  Verify target precedence, fog/input gates, scaling and independent review.
- [x] Tint hovered objects and give hovered gold its `_HIGHLIGHT` texture; let
  the Treasury/Heart store pile be picked and read "N Gold" (2026-10-23,
  [gold hover](../research/gold-hover-and-sparkle.md)). The store sparkle and
  lifting gold out of a store need simulation work there; owner comparison remains.

The current slice covers pointing, pickup, dig, held-item/gold, tagging, drop
and slap sprites, the blue terrain-following cage, the room/sell/door/trap placement
cage, tool icon and price, the keeper-spell and Query arms (icon and No Go, no price), and the Hand's held-item icons with the disposal bin (I-Dismiss) and No Go (research/placement-cursor.md), delayed short-name popups,
personal names and the top status strip. Owned-room storage/capacity fields
are integrated. Remaining special name/object producers and exact rendered-hit
and material contracts remain research/integration work; see
[cursor evidence](../research/hand-cursor-rendering.md) and
[hover text](../research/world-hover-text.md). Owner visual comparison remains.

## 6.3 — Bottom panel and keeper controls

- [x] Build the researched tabs, button states, room choices, costs, availability
  and selected-tool feedback. Room selection must be usable by name/icon without
  knowing room IDs or opening the Inspector.
- [x] Route construction/sell/Hand actions through existing simulation commands
  and rules. Add other panels against supported capabilities; record missing
  producers rather than manufacture successful-looking actions.
- [x] Prevent HUD clicks/drags/scrolls from also issuing world actions. Handle
  pointer capture, cancellation, focus loss and camera controls consistently.
- [ ] Review debug key bindings against [controls research](../research/controls.md)
  and the binary before presenting them as original DK2 controls. Keep diagnostics
  available without making them the player interface.

## Acceptance and boundaries

Built bottom-panel details: original room/spell/workshop ordering, researched
availability and cost feedback, Reaper/talisman state, secondary room cycling,
and the four creature views. Evidence and adapter ordering limitations live in
[bottom-panel-controls.md](../research/bottom-panel-controls.md) and
[creature-overview.md](../research/creature-overview.md).

Built 2026-10-23: the six indicator boxes (ADR-0532), the menu-40 text scrollbar (message panel scrolls, query panel bar inert as in DK2), Shift-click pinning of two bottom panels. Still open: owner comparison of the research, manufacture and
spell recharge sectors ([panel-indicators.md](../research/panel-indicators.md) §8); portrait hover previews (the tooltip text is built); full-map markers/legend;
the special creature name (+0x895) and the tile +0xB bit 0 "Enemy Wall" status
(door, trap, dead-body and Casino/Prison fixture status built 2026-10-23,
[world-hover-text.md](../research/world-hover-text.md)); Q197 and
the remaining Q611 modal producers. Options exposes Continue; its other rows
remain disabled until their corresponding frontends exist. These are not
successful-looking placeholder actions. Save/load/settings frontends require further
binary work. The hover red pass and its material are built; picking now follows DK2's
two stages (triangle hit, then centre, then the 3×3 sweep, each with its native
eligibility; [hand-cursor-rendering.md](../research/hand-cursor-rendering.md)); left: the
screen-circle radius, terrain occlusion, the object ownership arm and the resolver's
tile-hit and class-table gates. Status flowers have DK2's size, blend,
countdown, Imprison tag, Leaving/S-Gem and lifts; left: the casino Dollar mesh, the
trigger window (action 30), the global gate, state 0x110 and the disguise colour,
[status-flowers.md](../research/status-flowers.md) §3.

Model look, built 2026-10-23: DK2's additive environment-map pass on every model with a 0x40 material, and the loose Portal Gem's float, spin and once-a-second sparkle ([kmf-materials.md](../research/kmf-materials.md) §8, [objects.md](../research/objects.md) §8). Owner checks: Q622 (Lord corpse and armour shine), Q623 (gem), Q637 (shine look).

For each delivered slice: independent review, relevant headless presentation/input
tests, adapter build, and owner comparison at 1024×768 and 2560×1440 or above.
Check UI scaling, widescreen anchors, readable text, overlapping tooltips and
no commands passing through the HUD. Profile UI updates and minimap transfers;
avoid full tile-grid uploads when nothing changed.

Cursor hover work above is prioritized before the remaining panels. Status-flower
2D work follows §13 after the core slices; its unknown
3D/scale behaviour is researched before implementation. The front-end 3D scene,
audio, new game mechanics and replacement-art production are separate work.
Performance P0 remains [its own roadmap](2026-10-17-parallelism-design.md), not a
new requirement to parallelize the HUD before measuring it.
