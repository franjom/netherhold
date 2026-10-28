# Portal visibility (Q197)

Status: **Startup discovery solved; ring lighting remains partial. No new behavior built.** Binary and shipped-data
read, 2026-10-24. Confidence labels below distinguish the native contracts from
their possible correspondence to the owner's observation.

## Observation and data

The owner reports Hero Portals lit amid fog on Frosty's Lair, a dim surrounding
ring, a flashing minimap location and arriving mercenaries. The neighbouring
tile's tooltip says Fog of War. Follow-up screenshots show both entrance types
with their own short names and the nonowned-room status message. The earlier
attribution to TrapTime was an inference, superseded by this map identification.

**CONFIRMED by owner play, 2026-10-28 (Q669):** all three Mercenary Portals
and the keeper Portal are visible in a fresh Frosty's Lair skirmish before
exploring or casting Sight. On the minimap the room patch itself changes
colour; no separate flashing icon or outline was reported. This establishes
fresh-game visibility, not the exact native turn or operation that explores it.

**CONFIRMED:** Hero Portal is room25/terrain40; keeper Portal is room3/terrain12.
Neither terrain has bit24 (reveal through fog) or bit9 (AlwaysExplored). Their terrain
ambient RGB is `(115,97,65)` and light RGB `(0,0,0)`. Handler equality and the
canonical flag comparison are in [portals-mercenary.md §1](portals-mercenary.md#1-the-data-draws-no-distinction-at-all).

Frosty's Lair has three 3×3 Hero Portals centred at (15,52), (37,14), (57,59),
and a 3×3 keeper Portal at (37,37), all owner2. Each of their nine map cells has
the corresponding terrain and owner, followed by bytes `(1,1)`; that trailing
pair is the same throughout this 75×75 map. All four portal action points have
flags0 and trigger0. They are targets of the keeper roots' scripted reveals,
not self-running action-point roots.
The authored map contains no terrain27 Fog tiles, so a neighbour's Fog of War
label cannot be explained by that terrain being placed directly in this map.

## Scripted startup reveal (Q197, 2026-10-28)

**CONFIRMED from shipped data and native dispatch:** Frosty's Lair's trigger
chunk contains three conditions and six actions. Each keeper root tests level
time >= 0 (condition34, comparison5, value0, repeat1) and has two reveal actions:

| Keeper | Root | Action chain (unsigned stored ids) | Reveals |
|---|---:|---|---|
| 3 | 1 | 65535 → 65534 | AP1, Mercenary Portal (37,14); AP4, keeper Portal (37,37) |
| 4 | 2 | 65533 → 65532 | AP2, Mercenary Portal (15,52); AP4 again |
| 5 | 3 | 65531 → 65530 | AP3, Mercenary Portal (57,59); AP4 again |

Every action is type17, with body byte0 naming its action point and byte1=0.
Each point covers exactly its 3×3 entrance. These conditions are true on the
first player-tree evaluation; they do not wait for activation, arrival, Sight
or the player's approach. The stored action ids are negative signed shorts
when walked, as documented in [trigger-tree.md](trigger-tree.md).

The keeper loader `0x0051F0B0` passes the authored root to `0x004C7990`
(player `+0x10DA`). Walker `0x004C79A0` evaluates it; player condition dispatch
`0x004C7B20` delegates condition34 to `0x00518270` (time arm `0x0051829D`).
Player action dispatch `0x004C8620` delegates type17 to world arm `0x00519257`
in `0x00518AD0`. With byte1=0 the arm calls `0x00454B80` across the inclusive
rectangle. That writes explored `tile+8 bit0x2000`, expanding complete rooms
through `0x004E8B80`/`0x00454D40` where required.

There is no local-player guard on this world action. The three keeper roots
therefore expose all four entrance rectangles in the local view, including
the repeated central Portal reveal. Neighbouring tiles need not be explored.
This explains both entrance types without a general skirmish or fog exception.

**Existing implementation, inspected:** `TriggerTree` loads the player roots,
`TriggerExecutor` walks each root, condition34 compares level time, and action17
calls `DiscoveryRules.Rectangle` with explored=true. The latter applies the
rectangle to every represented viewer, the project's equivalent of the
unconditional native local-view write. No new simulation rule or HashVersion
change is needed from this finding; end-to-end Frosty's Lair acceptance was not
run. Work stopped before any state/hash change, as requested.

The earlier empty-trigger claim was false. The trigger chunk's content starts
at byte60 (`headerBlockSize=32`, content offset `28+headerBlockSize`), while
this level's THINGS content starts at56. A bounded reparse reaches all nine
records and the exact end of the 276-byte trigger file.

## 3D admission and ordinary exploration

**CONFIRMED:** `0x0043E320` substitutes Fog terrain27 for an unexplored,
unscanned tile outside temporary Sight and possession viewing. A scanned tile
without terrain bit24 substitutes ClaimScan terrain38. The real room-piece
builder is bypassed after that substitution. Terrain40 has no exception.
The admission path in `0x0043C310` and things/liquid path in `0x0043CC30`
also test exploration, temporary Sight and possession viewing. A centre light
does not bypass these tests in the bodies read.

**CONFIRMED:** `0x00454B80` delegates exploration of a room with construction
3,4 or8 to `0x004E8B80`, which walks its slab chain and calls `0x00454D40`
on each tile. Both 3×3 entrances qualify. Thus discovering one room tile can
explore all nine while its neighbouring ground remains fogged. This ordinary
rule already exists in `DiscoveryRules.SetExplored`; it is not a Hero Portal
exception. The level-start seed is documented in
[fog-reveal-sources.md §3](fog-reveal-sources.md#3-every-writer):
`0x0050C800` selects `0x00451FD0` or explore-all in modes2/3, and the seed
uses local ownership, non-solid neighbours and AlwaysExplored, without a
room25/terrain40 branch. Save-tile restoration `0x00452BD0` restores the saved
explored bit after terrain/room reconstruction rather than forcing portals on.

**CONFIRMED from the screenshots plus binary:** the hovered portal tiles are
explored. Both status producer `0x0042D1F0` and short-name producer
`0x0042D4A0` test tile `+9 bit0x20` before room dispatch; when clear they return
fog text, without a Sight or possession exception. The screenshots show room
names and nonowned-room status, so their hovered tiles pass that test.
The scripted rectangles above establish why these rooms are explored while
their neighbours remain fogged. Proximity to a keeper's Heart is not needed
to explain the four reveals.

## Entrance light and the ring

**CONFIRMED:** shared refresh `0x004DCD20` creates an engine light on the
fifth slab of a nine-slab room, at tile centre and raw height2000, through bridge
slot `+0xA4` (`0x004397E0`). Initializer `0x0043A1E0` resolves its inputs as
RGB `(255,33,26)`, intensity128, requested radius `0x2800` clamped to `0x2780`
(2.46875 tiles), and flags `0x15` before the initializer's flag additions.
The refresh clears bit0 at light `+0x72`. This applies to both entrance
types, without an arrival, activation or owner test in the refresh. It does
not write explored or scanned bits. Light `+0x72 bit0` is distinct from the
generic presentation flag `+0x72 bit0x10` tested for fog admission by the ordinary
rendered-object path (`0x0043C6E8`, `0x0043CF8E`). The cleared bit's purpose
remains unresolved; these inputs establish no special discovery mode.

**GUESS:** this light produces the reported ring. Its actual lighting footprint
and the meaning of that cleared light flag have not been traced to the fogged
surface draw. No separate light or fog bypass is implemented from this reading.

## Minimap flash

**CONFIRMED:** `0x0041A9B0` copies palette entries from phase `turn % 7` to
the neutral player's Number for the three ownership-colour banks. A visible
neutral room therefore changes colour independently of portal activation or
creature arrivals. Both entrance types use this ordinary room-class path;
unexplored terrain40 still resolves to unknown class1 in `0x00451C90`.
The cycle is already implemented in `MinimapPalette.AdvanceNeutralCycle`.
See [minimap-rendering.md](minimap-rendering.md) for palette/raster contracts.

The eight transient records in `0x0041CC50` are **battle** records, obtained
through `0x00509540` at world `+0x9C50 + index*0x26`, not entrance records.
Their green outline requires record `+0x1E`, local-player participation
(`0x005055D0`) and `+0x22 < 128`. The six target records and queued direction
sprites accept active-player Hearts or eligible creatures, not rooms25/3.
Battle-record structure has its home in [binary-census.md §3](binary-census.md#3-battles-are-presentation).

**STRONGLY INFERRED correspondence:** the owner's Q669 answer identifies the
changing room patch expected from the ordinary neutral palette cycle. The
native cycle itself is CONFIRMED above; exact colours and phase have not been
measured against play. There is no separate entrance marker to build from
this observation, and the neutral cycle is already implemented.

## Mercenary eligibility

**CONFIRMED:** `0x004DBF90` accepts creature definition `+0x686 bit0x400`
for room3, or `+0x68A bit8` (AvailableViaHeroPortal) for room25. This selects
arrival candidates. It is not a visibility flag. The ordinary creator
`0x004DC9A0`, constructor `0x004DBB90`, and special-arrival path
`0x004DCF50` have no direct explored/scanned write or minimap palette change
in the bodies read. The special predicate `0x004DD130` matches owned rooms
against a room-pattern table, not fog. This bounded reading does not exclude
ordinary sight from a creature after arrival.

## Earlier keeper-Portal minimap exclusions

This table preserves the keeper-only audit; it is not evidence that the owner's
Hero Portal display has been reproduced. TrapTime's `fow_1..3.SAV` had no
explored Portal tile. Level start rebuilds the class grid (`0x0050C058`) after
the fog seed (`0x0050B5D5`).

| Path | Confirmed scope |
|---|---|
| Grid getter `0x00508C70`, world slot `+0x1DC` | Constant-slot calls `0x004042AF`, `0x0041AAA2`, `0x0041AE7F` read classes into raster output; no write through the pointer. Address scans covered both executable sections and initialized blocks. |
| Rasters `0x0041A9B0` / `0x0041ADA0` | Class1 uses rock backdrop; other classes index the palette. No discovery-bit read or dim scanned palette. |
| Overlay `0x0041CC50` | Heart/reporting circle, Guard Post links, own type9 Guard Room rectangles, creatures, battles, traps, doors, Call to Arms and targets. No general room-icon arm. Other-player tile gates read explored. |
| Queued icons `0x0041FB40` / `0x0041F600` | Direction sprites for explored active-player Hearts or eligible live creatures. No Portal target. |
| Scanned readers | Offset/mask census found the known colour predicate and non-tile false positives. No direct reader in reviewed raster/overlay bodies. Highlight `0x00451A80` writes ordinary class or13. Not an exhaustive semantic classification of other readers. |
| Keeper definition / TrapTime | Portal flags lack bit24; no room/terrain override in that descriptor. Script exploration would set the explored bit, absent in these three saves. A different session or later script is not excluded. |

## Skirmish startup follow-up (2026-10-28)

**CONFIRMED:** the direct fog dispatch in `0x0050C800` applies to modes2/3 on
a fresh load. Fog switch `0x0075AE8B` selects explore-all or the local seed
`0x00451FD0`. The latter uses the local player getter (world slot `+0x264`,
`0x00508C60`, world `+0x7243`), ownership, orthogonal neighbours and terrain
AlwaysExplored. It has no portal/neutral-room reveal arm. This excludes that
specific fog-seed mechanism, not every indirect effect of mode initialization.

The level initializer `0x0050B5B0` also has a neutral-room pass. It supplies a
positive health/size delta to `0x004E5300`; the positive branch clamps health
and never transfers ownership or explores. Other inspected startup work is
also separate from discovery: `0x004F1040` sets pillar movement bit `+8 bit0x4000`,
`0x004526C0` writes the high nibble of tile `+2`, and `0x004BE370` clears level
statistics. The direct map-byte writes near `0x0050B79A`/`0x0050B867` modify
tile `+0xB bit0x20`, neither of the two discovery bits.

**CONFIRMED:** authored map callback `0x0051EC60` resolves the recorded player
and passes that player as the room-builder receiver (`0x0051ED93..0x0051ED9F`).
It does not temporarily build every entrance for the local player. Its final
map-byte flag branch changes tile `+8 bit0x1000`, not explored `0x2000`.
Room-thing loader `0x00521420` applies health/Heart/orientation work, without a
direct exploration write or mode-specific portal reveal in the body read.

The scripted startup reveal above settles the discovery source. A recheck of action-point
loader `0x005211A0` confirms that file flags at payload+18 supply runtime bit4,
the level-start reveal flag. The four authored points have that flag clear.
Room membership setter `0x00450770` and first-tile constructor `0x004EC5A0`
delegate terrain assignment through the existing owner; the bodies read have
no separate entrance exploration write. Their transitive callees remain bounded
by the earlier exclusions, not proved free of every indirect effect.

Other bounded controls: fresh map construction `0x0044E9E0` invokes tile
constructor `0x00452B30`, which clears explored and scanned state; it does not
carry room exploration from a previous match. Fog-off `0x00451EF0` explores
every tile, not entrances alone. Queued action121's `0x00514240` reveals a
specified player's entire territory through `0x004BFF60`; its located producer
`0x004267D0` is an other-player panel request, not the authored startup chain.
Fresh-level path `0x0050E620` and the reload action `0x00513870` converge on
the common initializer; the latter returns0 in mode3.

Executable control: Ghidra's DKII.EXE SHA256 matches the installed DKII.EXE.
The installed DKII-DX.exe has identical on-disk code sections; its only section
byte difference is the DDRAW/PATCH import-name substitution.

## Next evidence

Q197 retains the light/ring boundary; startup discovery is answered and Q669
is closed. Trace the bridge light's remaining flag through source submission
and compare the ring before building a lighting change. No simulation state,
HashVersion, presentation rules or Unity wiring changed.
