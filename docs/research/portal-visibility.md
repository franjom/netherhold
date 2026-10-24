# Portal visibility (Q197)

Status: **PARTIAL; no new presentation behavior built.** Binary and shipped-data
read, 2026-10-24. Confidence labels below distinguish the native contracts from
their possible correspondence to the owner's observation.

## Observation and data

The owner reports Hero Portals lit amid fog on Frosty's Lair, a dim surrounding
ring, a flashing minimap location and arriving mercenaries. The neighbouring
tile's tooltip says Fog of War. Follow-up screenshots show both entrance types
with their own short names and the nonowned-room status message. The earlier
attribution to TrapTime was an inference, superseded by this map identification.

**CONFIRMED:** Hero Portal is room25/terrain40; keeper Portal is room3/terrain12.
Neither terrain has bit24 (reveal through fog) or bit9 (AlwaysExplored). Their terrain
ambient RGB is `(115,97,65)` and light RGB `(0,0,0)`. Handler equality and the
canonical flag comparison are in [portals-mercenary.md §1](portals-mercenary.md#1-the-data-draws-no-distinction-at-all).

Frosty's Lair has three 3×3 Hero Portals centred at (15,52), (37,14), (57,59),
and a 3×3 keeper Portal at (37,37), all owner2. Each of their nine map cells has
the corresponding terrain and owner, followed by bytes `(1,1)`; that trailing
pair is the same throughout this 75×75 map. All four portal action points have
flags0 and trigger0; the shipped trigger file contains no action records.
These files establish no portal-specific scripted reveal.

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
Ordinary whole-room exploration explains why the room can be visible while
its neighbour remains fogged; the source that first explored it remains unknown.
The portal at (37,14) borders owner3's Heart area, but no native discovery state
was captured and that proximity alone establishes no cause.

## Entrance light and the ring

**CONFIRMED:** shared refresh `0x004DCD20` creates an engine light on the
fifth slab of a nine-slab room, at tile centre and raw height2000, through bridge
slot `+0xA4` (`0x004397E0`). Its remaining literals are `0xFF,0x21,0x1A,0x80,
0x2800,0x15`; it clears bit0 at light `+0x72`. This applies to both entrance
types, without an arrival, activation or owner test in the refresh. It does
not write explored or scanned bits. These light parameters remain undecoded
here; they are not evidence of a special fog mode.

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

**GUESS:** the reported flashing patch is the ordinary neutral palette cycle.
This has not been matched to the owner's display. A changing room-colour patch
and a separate marker are different observations; Q650 asks which is present.
There is no newly established Hero Portal marker to build.

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

## Remaining boundary

Q197 stays open. Q650 asks when the entrances first become visible and what
shape flashes in a fresh original game. Next identify the discovery source
and trace the bridge light's flag through surface lighting before attributing
an exception to room25. No simulation state,
HashVersion, presentation rules or Unity wiring changed.
