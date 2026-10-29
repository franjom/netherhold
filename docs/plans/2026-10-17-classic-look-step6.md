# Step 6 — HUD and controls

Status: the gameplay HUD, Options/save/load/settings, full-map preview and
markers/legend, objects-only picking and native camera/key consumers are built
on `codex/hud-completion-final` (2026-10-29). Implementation and runtime
verification are tracked in the [completion plan](2026-10-29-hud-and-front-end-completion.md).
Owner comparison remains open; ADR-0497 remains proposed pending that decision.
Step5's visual/performance gates remain in [its checklist](2026-10-14-classic-look-step5.md).

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
- Owner comparison: intro/modal restoration in original DK2 (Q648/Q649); Q611's
  direct producer audit and campaign gate subset are in hud-visibility.md.
- [x] Map the shipped widgets/fonts used by the built HUD and Options through
  asset providers. Original/derived artwork remains outside Git.
- Deferred research: remaining overlay producers and secondary controls are
  bounded by the current records and [minimap research](../research/minimap-rendering.md).
- Deferred research: remaining layout/input paths. Q197's startup/minimap mechanism is
  established in [portal-visibility.md](../research/portal-visibility.md); its light-source count is verified and entrance lighting built. `0x00427BA0` lays out only indicator menus45-50 (built: research/message-tabs.md §9).
- [x] Pilot UI Toolkit's custom minimap shader integration and `.bf4` font quality
  at reference and large sizes. Record results before selecting production UI
  plumbing; an alternative toolkit requires the ADR-0497 amendment it describes.

Rendering feasibility passed in an isolated URP project at both reference and
1440p sizes; [pilot checks, captures and limits](../../tools/hud-probe/README.md).
The native raster/overlay reading is in [minimap-rendering.md](../research/minimap-rendering.md).
Remaining interactions, complete widget/art mapping and production font metrics remain
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
to the disc; unbuilt overlay producers remain explicit in the research. Whole-HUD
camera/letterbox and campaign result gates are built; Q611's continuation limits
and original-game checks are in [hud-visibility.md](../research/hud-visibility.md).
Disc/frame layout and rotation still need owner comparison. Static native menu
and corner-art mapping and default primary/secondary bindings are recorded;
Query/Sell/Options corner bindings, Heart/GUI/full-map hotkeys and secondary
disc entry to the full map are integrated; see
[minimap-controls.md](../research/minimap-controls.md).

- [x] Present changed tile classes through a renderer-independent view; draw the
  palette, rotated map, mask and background on the GPU as specified in §13.
- [x] Add researched creature/trap/door markers, reporting and pulse outlines,
  zoom, camera footprint and primary navigation, preserving discovery rules.
- [x] Build the researched map/corner controls; unsupported overlay producers
  remain deferred in the research.
- Owner comparison: disc/frame/rotation at the same DK2 location; use the
  [capture checklist](../research/hud-comparison-checklist.md).

## 6.2 — Top bar

- [x] Present settled gold, mana, separate incoming/outgoing rates, the Heart
  health ring and supported hover/status text using original frames and sprites.
- [x] Complete resource hover/clicks, payday countdown and wage-bill flashing,
  damage alarms and message flashing. Native evidence and remaining producers:
  [hud-resources.md](../research/hud-resources.md).
- Owner comparison: anchors, content, states and timing; use the capture checklist.

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
  lifting gold out of a store are built (ADR-0534); owner comparison remains (Q641).

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
- [x] Replace conflicting debug shortcuts with researched keys and bind supported
  actions through Keyboard Setup; see [controls research](../research/controls.md).

## Acceptance and boundaries

Closed 2026-10-24: portrait hover previews. The binary shows the unchanged portrait,
existing count highlight and status text; no additional preview was built. Read and
play-only limits: [creature overview](../research/creature-overview.md#portrait-and-count-hover-text).

Built bottom-panel details: original room/spell/workshop ordering, researched
availability and cost feedback, Reaper/talisman state, secondary room cycling,
and the four creature views. Evidence and adapter ordering limitations live in
[bottom-panel-controls.md](../research/bottom-panel-controls.md) and
[creature-overview.md](../research/creature-overview.md).

Built 2026-10-23: the six indicator boxes (ADR-0532), the menu-40 text scrollbar (message panel scrolls, query panel bar inert as in DK2), Shift-click pinning of two bottom panels. Still open: owner comparison of the research, manufacture and
spell recharge sectors ([panel-indicators.md](../research/panel-indicators.md) §8),
remaining layout/input paths and Q611's continuation/restoration boundary.
Full-map preview, markers, legend, Lord names and Enemy Wall status are built;
Enemy Wall reuses the existing Secret Door tile flag without a hash change.
Options objective, save/load, restart/end and the native Game Options family are
built; unresolved native policies remain Q683. See [hover text](../research/world-hover-text.md)
and [Options](../research/options-menu.md). The hover red pass and its material are built; picking now follows DK2's
two stages (triangle hit, then centre, then the 3×3 sweep, each with its native
eligibility; [hand-cursor-rendering.md](../research/hand-cursor-rendering.md)); the screen circle,
the object ownership arm and the resolver's gates are built or mapped (2026-10-24); left:
terrain occlusion (owner comparison). Objects-only picking (default PAGE UP) is
built. Status flowers include the casino Dollar mesh and action30's trigger
window alongside size, blend, countdown, Imprison, Leaving/S-Gem and lifts.
Remaining research: the global gate, state0x110 and disguise colour,
[status-flowers.md](../research/status-flowers.md) §3.

Model look, built 2026-10-23: DK2's additive environment-map pass on every model with a 0x40 material, and the loose Portal Gem's float, spin and once-a-second sparkle ([kmf-materials.md](../research/kmf-materials.md) §8, [objects.md](../research/objects.md) §8). Owner checks: Q622 (Lord corpse and armour shine), Q623 (gem), Q637 (shine look).

For each delivered slice: independent review, relevant headless presentation/input
tests and adapter build. Owner comparison at1024×768 and2560×1440 remains
in the capture checklist; prepared Unity images do not close acceptance.
Check UI scaling, widescreen anchors, readable text, overlapping tooltips and
no commands passing through the HUD. Profile UI updates and minimap transfers;
avoid full tile-grid uploads when nothing changed.

Cursor hover work above is prioritized before the remaining panels. Status-flower
2D work follows §13 after the core slices; its unknown
3D/scale behaviour is researched before implementation. The front-end 3D scene,
new game mechanics and replacement-art production are separate work. F8 audio is wired
(ADR-0583); Unity hearing acceptance remains in the front-end plan.
Performance P0 remains [its own roadmap](2026-10-17-parallelism-design.md), not a
new requirement to parallelize the HUD before measuring it.
