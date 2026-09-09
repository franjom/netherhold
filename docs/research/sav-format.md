# Format: saved games (`.SAV`, `.QSAV`)

Source(s):
- Direct parsing of the reference install's own saves
  (`Data/Save/MyPetDungeo.SAV`, `Data/Save/MPDQuickSave.QSAV`, and the owner's
  purpose-built eight-save series, 2026-09-06), cross-validated against
  `Data/editor/maps/mpd1Map.kld`
- Retail v1.70 `DKII.EXE`, for the save routine itself

Original version: DK2 v1.70 (GOG)

Saves are user-generated, not shipped assets, so nothing here is a
redistribution question. Reading them still goes through the asset
abstraction layer like everything else.

## Shape of the file — CONFIRMED

| Region | Offset | Size | Contents |
|---|---|---|---|
| Header | `0x0000` | 0x134 | build stamp, level name, game tick |
| Global/player block | `0x0134` | varies by level | per-player state; gold at `0x2dd4` |
| Tile grid | located, not fixed | `width*height*10` | the dungeon |
| Entity sections | after the grid | rest of file | typed, variable-size, self-serialising |

**The grid offset is not a constant.** The two original samples were saves of
the same level, so two samples were one sample. Across all fourteen saves in
the reference install the grid lands on three offsets:

| Level | Grid | Offset | Saves |
|---|---:|---:|---:|
| `mpd1` | 61x61 | `0x8861` | 10 |
| `TrapTime` | 110x80 | `0x14880` | 3 |
| `Caverns` | 101x101 | `0x19e0f` | 1 |

It is **not a function of the tile count** either — those offsets divided by
their tile counts give 9.38, 9.56 and 10.39 bytes a tile, and no linear fit
passes through all three. What varies is the global/player block above it, which
is sized by something else about the level (Q183).

`0x8861` is odd, so the writer is a byte-oriented serialiser, not a `memcpy` of
a packed struct.

## How the save is written — CONFIRMED from the binary

The save routine was found by following the Save window's text-input handler
(`0x0042A920`) through **vtable slot `+0x28`** on the world object, whose
vtable is at `0x0066E3EC`. That slot opens the file and hands it to the
serialiser at **`0x0050AA60`**, which writes the whole world through one
"write N bytes" primitive at `0x005B3EE0`.

*Provenance: the vtable membership and the addresses came from the Flame symbol
map, which is a structural index of this same executable; the write order below
was read from the decompiled routine. Nothing here is anybody's source
(ADR-0046).*

The routine writes, in order:

| Offset | Size | What |
|---|---:|---|
| `0x00` | 4 | the literal **220** |
| `0x04` | 4 | the literal **1** |
| `0x08` | **64** | `__DATE__` then `__TIME__`, concatenated |
| `0x48` | 122 | opaque, from the world object |
| `0xC2` | **64** | the **level name**, NUL-padded |
| `0x102` | 4 | the **current game tick** |
| `0x106` | 4 | opaque, from the world object |
| `0x10A` | 42 | opaque, from the world object |

so the header ends at **`0x134`**. Every field lands exactly where the writer
says in both reference saves, the level name at `0xC2` included.

**220 is not a header length.** It is a literal the writer emits; it happens to
fall above the level name, which is why reading it as a size ever worked.
`Dk2SaveReader.ExpectedFormatWord` treats it as a magic word and `HeaderLength`
is `0x134`.

**The game tick is a real, useful field**: 8480 in the sample save and 2598 in
its earlier quicksave.

The build stamp is DK2's own version guard and this reader's too:
`Dk2SaveReader.SupportedBuildStamp` is `"May 12 200012:28:22"`, and
`Dk2SaveHeader.IsSupportedBuild` reports whether the writing build is the one
whose layout is proven.

### The header leaks DK2's stack, and that answers Q180

The build stamp is a **64-byte** field but the text is 19 characters. The writer
builds it in a 64-byte stack buffer with a copy and a concatenate and never
clears the rest, so **every DK2 save carries about 44 bytes of uninitialised
stack**.

That is the whole of what looked like a serialised object graph. The addresses
at `0x20`, `0x28`, `0x34` and `0x3c` all sit inside that buffer past the
terminator, and they behave exactly as stack garbage should: the image address
`0x006722D0` is identical in both saves because it is a static, while the heap
ones differ (`0x036F7D70` against `0x036FAC70`) because the two saves were
written by different runs.

**So Q180 is answered: no.** The save does not store raw pointers, needs no
address-fixup pass, and the object graph is not address-linked.

### Then, in order, each subsystem writes itself

Resources, two objects reached through vtable slot `+4`, the **rooms**, the
**creature collection**, the world's own block, 24 raw bytes, the camera, and
the player message queue.

## Tile grid — CONFIRMED

Row-major `width * height`, dimensions taken from the level's own
`...Map.kld` (61x61 for mpd1), **10 bytes per tile** — the runtime tile, wider
than the 4-byte design tile in the `.kld`.

**Four lanes are identified, and each was settled against something outside the
save file.** The owner supplied a purpose-built series on 2026-09-06 — five
saves of one dungeon differing by one known action each — which is what made
the last two readable:

| Lane | Meaning | How it was settled |
|---:|---|---|
| 0 | terrain id, the MAP(100) enum | 73%/85% agreement with the level's design map |
| **2** | **room instance id**, 0 for none | every distinct value is one solid rectangle matching its room type's footprint |
| **6-7** | **tile health**, little-endian u16 | equals the terrain's `StartingHealth` from `Terrain.kwd` on **91.9%** of tiles |
| 8 | owner / player id | 72% agreement with the design map's player byte |

`Dk2SaveReader` names them `LaneTerrain`, `LaneRoom`, `LaneHealth` and
`LaneOwner`; `SavedTile.Raw` carries all ten bytes so a partial reading loses
nothing.

### The control that made the series work

Between two of the saves the owner created an imp and did nothing else. **Zero
tiles changed** in lanes 0, 2, 4, 5, 6, 7 and 8 — which both validated the
method and showed that lanes 1, 3 and 9 are noise for this purpose: those three
move across most of the map between *any* two saves, consistent with visibility
or fog state (Q181). **Lanes 4 and 5 are non-zero only on room tiles** and are
otherwise unread.

Read as bytes those three lanes look like noise; bit by bit they are quiet.
**Lane 1, bit 6 is the local player's discovery bit** — set on 100% of the
owner's own territory and 0% of the other three keepers', monotonic with zero
reversals across three saves, and clearing in a band **13 tiles tall around a
1-tile tunnel**, which is the six-tile `PerceptionRange` every creature record
carries. `fog-of-war.md` §11 has the whole derivation. Lane 3's high nibble
resets to zero wherever *any* keeper is active, in every territory at once, so
it is a property of the tile rather than of a viewer — still UNKNOWN, and no
longer a fog candidate.

Mining one gold tile and claiming one wall changed exactly **three** slots:
terrain 6 (Gold) became 8 (Claimed Path) on the mined tile, the claimed tile's
owner went 2 (neutral) to 3 (the keeper) — and the third was the scratch slot
below. Mining two changed five. The arithmetic closes exactly.

### The series also reconfirmed two settled rules

A cross-check that the save data reproduces what the original was already
observed to do, not news about DK2:

- **Mining a gold seam yields its full 3000.** Gold went 3000 to 6000 across
  one mined seam and 6000 to 12000 across two, and `Terrain.kwd` gives terrain 6
  a gold value of 3000 (Q21).
- **A room's cost is per tile**, twice over. The 5x5 hatchery took gold from
  12000 to 4500 — 7500 for 25 tiles, i.e. **300 a tile**, and the ROOMS record
  prices a Hatchery at exactly 300. A later 5x5 lair took 13250 to **5750**, the
  same 7500 for 25 tiles, and a Lair is priced 300 too (Q32). The lair's 25
  tiles came out of Claimed Path, whose count falls 41 to 16 across the same
  pair of saves.

That recorded gold sequence — 3000, 6000, 6000, 12000, 4500 — is also what
located the player's gold at **`0x2dd4`** in the global/player block: it matches
at exactly two offsets in the whole file, one there and one inside the entity
region. Q183 is partly answered by it, and the same trick will name more fields
from any save series with recorded numbers.

### Lane 2 is an instance, not a type

The type is already in the terrain id. In the save with a purpose-built 5x5
hatchery, lane 2 yields **ten** instances, every one a solid rectangle whose
size matches what it is: a 2x2 hero gate on 4 tiles, four 3x3 portals on 9
each, and five 5x5 rooms — dungeon heart, guard room, work shop, hatchery — on
25 each. A byte that merely correlated with rooms could not do that.
`Dk2SaveReaderTests.RoomId_GroupsTilesIntoContiguousRoomsOfTheRightSize` pins it.

**A room's tile set includes part of its surrounding wall.** The hatchery's 25
tiles are 20 hatchery floor plus 5 of wall and claimed path. **Open detail, with
no question row of its own:** five hatchery-floor tiles in the adjoining column
carry room id 0, so the room instance and the terrain-13 footprint are offset by
one column. Whether that is a room still being registered at save time or
something about how DK2 assigns the id is not settled.

### Health, and the residue

91.9% of tiles on a freshly started dungeon carry their terrain's
`StartingHealth` exactly, and 82% on a long-played one — the difference being
tiles mid-dig, mid-claim or mid-reinforce, which is what health is for. Across
the owner's eight-save series the figure moves 91.8% to 91.3% as the dungeon is
worked.

**The residue splits in two, and only one half is a puzzle.**

*Variable mismatches* track play. Rock carrying Gold's 660, Rock carrying
Reinforced Wall's 900, Reinforced Wall carrying Rock's 165 — the counts move
between every pair of saves and follow the tiles the owner dug, claimed and
reinforced. That is health doing its job.

*A fixed anomaly of exactly 44 + 44 tiles* does not. 44 tiles of Impenetrable
Rock carry Rock's 165, and 44 tiles of Rock carry Impenetrable Rock's 1, and
**those two counts are identical in all eight saves** — untouched by mining,
claiming, reinforcing or building.

**Lane 0 is not the problem.** The level's own design map agrees with lane 0 on
all 88 tiles — 44 Impenetrable Rock, 44 Rock, no exceptions — so the terrain
reading is sound and it is the health that is unexpected. Lane 0 is not half of
a two-field terrain.

What the 88 tiles are is open (Q189). Both groups lie along arcs, and their lane
profiles are identical down to the counts — lane 1 splits 37/7 in both, lane 3
splits 27/14/2/1 in both — yet no mirror, rotation or transpose maps one onto
the other. Their design-map bytes are also identical to each other and to
ordinary tiles of the same terrain, so nothing in the `.kld` marks them out.

### The last slot of the grid is not a tile

`(60,60)` on a 61x61 level changes between saves where **nothing on the map
changed**, carries a non-zero room id of its own, and is the one slot whose
owner byte falls outside the level's player set. Treated as scratch and excluded
from tile statistics: `Dk2SaveReader.ScratchSlotIndex(width, height)` is
`width * height - 1`.

## The entity region — Q182 answered, and the premise was wrong

**There is no 189-byte record, and there is no flat array.** The creature
collection's writer at **`0x004B6040`** emits a run of **typed sections**, each
one:

```
u32 kindId
u32 count
then `count` entities, each of which serialises ITSELF
```

Every entity is written by a virtual call through slot `+4` on the entity, so
**records are variable-size and per-class**. The kind ids the writer emits are
`200`, `196`, `195`, `194`, `262`, `202`, `197`, `203` — all in the
**THINGS(190)** family of the KWD record numbering, with `262` in the
SHOTS(260) range. The save reuses the shipped data files' own type numbers.

### What the 189 really was

An artefact, and a warning worth keeping. Two mistakes stacked:

1. The **first** measurement (83.7% agreement) was inflated by the region being
   **84.3% zero bytes** — zeros agree at every lag, and arbitrary lags scored
   ~73% on the same data.
2. Re-measured excluding zero-zero pairs, lag 189 really did stand out — 11.4%
   against a ~1.5% background, with 378 as its harmonic. That was a **real
   periodicity in the data** and it still was not a record size. Mixed
   variable-length sections, most of them sparse, produce exactly that shadow.

**Statistics over a container cannot tell you a struct.** Two independent
measurements agreed with each other and both were answering the wrong question.

The offsets once recorded here as a "region header" — counts at `+0x00`,
`+0x1a`, `+0x1e` — fall inside entity records instead (Q187). Two of the values
give it away: `+0x44` held 8478 and 2597 against game ticks of 8480 and 2598,
so they are per-entity tick stamps, one or two ticks behind the save.

### What it takes to read entities

Each entity class's own serialiser, one per kind id — eight-plus separate
structs rather than one, bounded now that the dispatch is understood, and to be
read off the routine rather than off the bytes.

## The grid is located, not assumed

`Dk2SaveReader.FindGridOffset` searches for the grid instead of reading it at a
constant, by correlating lane 0 against the level's design map — the MAP(100)
chunk of `<Level>Map.kld`, byte 0 of each 4-byte design tile, via
`GameDatabaseLoader.LoadCampaignMap` — in two passes: score a spread
sample of `SampleTiles` = 512 tiles at every candidate offset, then score the
`FinalistOffsets` = 32 best against the whole grid and take the winner. A near
neighbour can win the sample, so the sample alone is not trusted.

Agreement is not a fact about the save but the read's own sanity check
(`SavedGame.TerrainAgreement`). A mid-game save scores lower simply because the
dungeon has been dug, which is what the 73–85% figures are; it is not parser
error. **The peak is sharp**, measured across all fourteen saves in the install:

| Read at | Agreement |
|---|---:|
| the located offset | **72.9% – 100%** |
| one tile record either side | 59.1% – 91.3% |
| **one byte** on | **0.000%**, every save |

The one-byte control is the strong one. Shifting by a whole record still agrees
at up to 91% because a map has long runs of a single terrain, so a tile mostly
resembles its neighbour. Breaking the **stride** instead puts every lane against
the wrong field of the wrong tile, and that agrees with nothing at all — which
pins the 10-byte record as firmly as the offset.
`Dk2SaveReaderTests.Grid_ReadAtTheWrongOffset_DoesNotAgree` asserts both halves.

**What the search costs.** Every offset from the end of the header to the last
that could hold a grid, at 512 comparisons each, then 32 full passes: a few
hundred milliseconds on the largest save here. Paid once per load.

## What this buys us

`GameDatabaseLoader` already yields a `LevelMap`, and the Unity adapter's
`DungeonRunner` already renders one. `Dk2SaveReader.Load` fills a `LevelMap`
from lanes 0 and 8 and needs no Unity-side change. Entities need the per-class
serialisers first.

Writing saves back is not attempted and is not planned.

## Implementation

`DK2.Data.Save.Dk2SaveReader`, `Dk2SaveHeader`, `SavedGame`, `SavedTile`.

## Tests

`DK2.Data.Tests.Dk2SaveReaderTests` — the header and build guard, grid
agreement with the design map and its two negative controls, every installed
save loading, owner ids being reserved, terrain ids resolving against the
database, health matching `StartingHealth` on undamaged tiles, and room ids
grouping into contiguous rooms of the right size.

## Open questions

Q181 (lanes 1, 3, 9 and 4–5), Q183 (the rest of the global/player block),
Q189 (the 44 + 44 health anomaly), and the one-column offset between a room
instance and its terrain footprint, which has no question row.
