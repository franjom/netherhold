# Format: saved games (`.SAV`, `.QSAV`)

Source(s):
- Direct parsing of the reference install's own saves
  (`Data/Save/MyPetDungeo.SAV`, `Data/Save/MPDQuickSave.QSAV`, 2026-09-06),
  cross-validated against `Data/editor/maps/mpd1Map.kld`

Original version: DK2 v1.70 (GOG)

Saves are user-generated, not shipped assets, so nothing here is a
redistribution question. Reading them still goes through the asset
abstraction layer like everything else.

## Shape of the file — CONFIRMED

Three regions. The first two are fixed-size; only the third varies.

| Region | Offset | Size | Contents |
|---|---|---|---|
| Header | `0x0000` | 220 (`0xdc`) | build stamp, level name |
| Global/player block | `0x00dc` | to `0x8861` | not yet decoded |
| Tile grid | `0x8861` | `width*height*10` | the dungeon |
| Entity records | `0x119bb` | rest of file | 189-byte records |

The grid begins at `0x8861` in **both** sample files even though they are
264,374 and 170,058 bytes — so everything before the entity list is at a fixed
offset. The whole 94 KB size difference between the two saves is in the entity
region.

Note `0x8861` is odd, so the writer is a byte-oriented serialiser, not a
`memcpy` of a packed struct.

## How the save is written — CONFIRMED from the binary

The save routine was found by following the Save window's text-input handler
(`0x0042A920`) through **vtable slot `+0x28`** on the world object, whose
vtable is at `0x0066E3EC`. That slot opens the file and hands it to the
serialiser at **`0x0050AA60`**, which writes the whole world through one
"write N bytes" primitive at `0x005B3EE0`.

*Provenance: the vtable membership and the addresses came from the Flame symbol
map, which is a structural index of this same executable; the write order below
was read from the decompiled routine. Nothing here is anybody's source.*

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

**220 is not a header length.** It is a literal the writer emits, and this
project read it as a size until the routine was decompiled — which happened to
work only because the level name falls inside the first 220 bytes.

**The game tick is a real, useful field**: 8480 in the sample save and 2598 in
its earlier quicksave.

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
the player message queue. The tile grid falls inside the early group, which is
why it lands at a fixed offset.

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

### The control that made the series work

Between two of the saves the owner created an imp and did nothing else. **Zero
tiles changed** in lanes 0, 2, 4, 5, 6, 7 and 8 — which both validated the
method and showed that lanes 1, 3 and 9 are noise for this purpose: those three
move across most of the map between *any* two saves, consistent with visibility
or fog state, and they are still UNKNOWN.

Mining one gold tile and claiming one wall changed exactly **three** slots:
terrain 6 (Gold) became 8 (Claimed Path) on the mined tile, the claimed tile's
owner went 2 (neutral) to 3 (the keeper) — and the third was the scratch slot
below. Mining two changed five. The arithmetic closes exactly.

### The series also reconfirmed two settled rules

Not new, but worth recording as a cross-check that the save data reproduces
what the original was already observed to do:

- **Mining a gold seam yields its full 3000.** Gold went 3000 to 6000 across
  one mined seam and 6000 to 12000 across two, and `Terrain.kwd` gives terrain 6
  a gold value of 3000 (Q21).
- **A room's cost is per tile**, twice over. The 5x5 hatchery took gold from
  12000 to 4500 — 7500 for 25 tiles, i.e. **300 a tile**, and the ROOMS record
  prices a Hatchery at exactly 300. A later 5x5 lair took 13250 to **5750**, the
  same 7500 for 25 tiles, and a Lair is priced 300 too (Q32). The lair's 25
  tiles came out of Claimed Path, whose count falls 41 to 16 across the same
  pair of saves.

Both were already confirmed from the owner's play; the arithmetic closing to the
gold piece is a check on this reader rather than news about DK2.

### Lane 2 is an instance, not a type

The type is already in the terrain id. In the save with a purpose-built 5x5
hatchery, lane 2 yields **ten** instances, every one a solid rectangle whose
size matches what it is: a 2x2 hero gate on 4 tiles, four 3x3 portals on 9
each, and five 5x5 rooms — dungeon heart, guard room, work shop, hatchery — on
25 each. A byte that merely correlated with rooms could not do that.

**A room's tile set includes part of its surrounding wall.** The hatchery's 25
tiles are 20 hatchery floor plus 5 of wall and claimed path. **Open detail:**
five hatchery-floor tiles in the adjoining column carry room id 0, so the room
instance and the terrain-13 footprint are offset by one column. Whether that is
a room still being registered at save time or something about how DK2 assigns
the id is not settled (Q188).

### Health, and the residue

91.9% of tiles on a freshly started dungeon carry their terrain's
`StartingHealth` exactly, and 82% on a long-played one — the difference being
tiles mid-dig, mid-claim or mid-reinforce, which is what health is for. Across
the owner's eight-save series the figure moves 91.8% to 91.3% as the dungeon is
worked, exactly as it should.

**The residue splits in two, and only one half is a puzzle.**

*Variable mismatches* track play. Rock carrying Gold's 660, Rock carrying
Reinforced Wall's 900, Reinforced Wall carrying Rock's 165 — the counts move
between every pair of saves and follow the tiles the owner dug, claimed and
reinforced. That is health doing its job.

*A fixed anomaly of exactly 44 + 44 tiles* does not. 44 tiles of Impenetrable
Rock carry Rock's 165, and 44 tiles of Rock carry Impenetrable Rock's 1, and
**those two counts are identical in all eight saves** — untouched by mining,
claiming, reinforcing or building.

**Lane 0 is not the problem, which was the thing worth ruling out.** The
level's own design map agrees with lane 0 on all 88 tiles — 44 Impenetrable
Rock, 44 Rock, no exceptions. So the terrain reading is sound and it is the
health that is unexpected. The earlier worry that lane 0 might be half of a
two-field terrain is answered: it is not.

What the 88 tiles are is still open (Q189). Both groups lie along arcs, and
their lane profiles are identical down to the counts — lane 1 splits 37/7 in
both, lane 3 splits 27/14/2/1 in both — yet no mirror, rotation or transpose
maps one onto the other. Their design-map bytes are also identical to each
other and to ordinary tiles of the same terrain, so nothing in the `.kld`
marks them out.

### The last slot of the grid is not a tile

`(60,60)` on a 61x61 level changes between saves where **nothing on the map
changed**, carries a non-zero room id of its own, and is the one slot whose
owner byte falls outside the level's player set — which is what the earlier
"one unowned tile" oddity actually was. Treated as scratch and excluded from
tile statistics.

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

An artefact, and a warning worth keeping. Two separate mistakes stacked:

1. The **first** measurement (83.7% agreement) was inflated by the region being
   **84.3% zero bytes** — zeros agree at every lag, and arbitrary lags scored
   ~73% on the same data.
2. Re-measured excluding zero-zero pairs, lag 189 really did stand out — 11.4%
   against a ~1.5% background, with 378 as its harmonic. That was a **real
   periodicity in the data** and it still was not a record size. Mixed
   variable-length sections, most of them sparse, produce exactly that kind of
   shadow.

The lesson is the project's own rule, arrived at the expensive way: **statistics
over a container cannot tell you a struct.** Two independent measurements agreed
with each other and both were answering the wrong question. The write order came
out of the binary in one pass once the right function was found.

### What it takes to read entities

Each entity class's own serialiser, one per kind id. That is bounded work now
that the dispatch is understood, but it is eight-plus separate structs rather
than one, and it should be read the same way this was: off the routine, not off
the bytes.

The earlier "region header" recorded here — counts at `+0x00`, `+0x1a`, `+0x1e`
and so on — **was not a header at all.** Those offsets fall inside entity
records. Two of the values give the reading away: `+0x44` held 8478 and 2597
against game ticks of 8480 and 2598, so they are per-entity tick stamps, one or
two ticks behind the save.

## What this buys us

`GameDatabaseLoader` already yields a `LevelMap`, and `DungeonRunner` already
renders one. A save reader that fills a `LevelMap` from lanes 0 and 8 needs no
Unity-side change at all. Entities need the 189-byte record decoded first.

Writing saves back is not attempted and is not planned.
