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
the same level, so two samples were one sample. Across fourteen reference saves plus the owner Level 10 save in
the reference install the grid lands on three offsets:

| Level | Grid | Offset | Saves |
|---|---:|---:|---:|
| `mpd1` | 61x61 | `0x8859` | 10 |
| `TrapTime` | 110x80 | `0x14878` | 3 |
| `Caverns` | 101x101 | `0x19e07` | 1 |
| `level10` | 58x100 | `0xE7DA` | 1 |

It is **not a function of the tile count** either — those offsets divided by
their tile counts give 9.38, 9.56 and 10.39 bytes a tile, and no linear fit
passes through all three. What varies is the global/player block above it, which
is sized by something else about the level (Q183).

`0x8859` is odd, so the writer is a byte-oriented serialiser, not a `memcpy` of
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

## Tile grid - STRONGLY INFERRED from the binary, checked against saves

The writer `0x0044F3F0` emits an eight-byte map header, then calls
`0x00452B90` once per tile. Each call writes ten bytes from runtime tile
`+2..+0xB`, row-major. The record begins at the room word. In the owner's
Level 10 save the header is at `0xE7D2`, so the first record is `0xE7DA`.

| Record offset | Reading | Evidence |
|---:|---|---|
| 0-1 | room instance, low twelve bits | writer source `tile+2`; the Level 10 Portal's nine tiles all have id 1125 |
| 2-3 | undecoded | writer source `tile+4` |
| 4-5 | health, u16 | writer source `tile+6`; pristine Rock is 165 and Heart 10000 in the save |
| 6-7 | owner, low twelve bits; high bits are flags | writer source `tile+8`; player ids agree with the map and the claimed Portal |
| 8 | terrain id | writer source `tile+0xA`; all 5,800 Level 10 tiles match the design map |
| 9 | undecoded | writer source `tile+0xB` |

The former reader located the terrain lane itself at `0xE7E2` and called
that the record start. This shifted room, health, and owner to the next tile.
The old lane-1 discovery reading is actually record lane 9; the old
lane-3 and lane-4/5 readings belong to the following tile. The
44+44 health mismatch, the supposed last scratch slot, and the one-column
room footprint offset were consequences of that shift. They are not game
rules. Q181 and Q189 track any remaining lane questions.

`Dk2SaveReader.FindGridOffset` now scores terrain at record `+8` and returns
the actual record start. `ReadTiles` masks both room and owner words to
twelve bits, and `ReadGrid` decodes the same owner and terrain. Every
`width * height` record is a tile, including the last one. The owner save
has a real border tile there (terrain 30, owner 2).

The corrected alignment resolves Q200 too. In `fow_3.SAV` (TrapTime), Stone
Bridge tiles (72,38) and (76,38) have room ids 1117 and 1116. The Claimed
Path tiles between them have room id zero. The former reader attributed each
bridge id to an adjacent path tile.

The owner's five-save action series still confirms its observed outcomes:
one mined gold seam added 3000, and a 5x5 Hatchery or Lair cost 7500.
Those economy rules are independently supported by the shipped terrain
and room records (Q21, Q32). The old per-lane change counts from the
shifted alignment should not be used to infer fields.

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
constant, by correlating record lane 8 against the level's design map — the MAP(100)
chunk of `<Level>Map.kld`, byte 0 of each 4-byte design tile, via
`GameDatabaseLoader.LoadCampaignMap` — in two passes: score a spread
sample of `SampleTiles` = 512 tiles at every candidate offset, then score the
`FinalistOffsets` = 32 best against the whole grid and take the winner. A near
neighbour can win the sample, so the sample alone is not trusted.

Agreement is not a fact about the save but the read's own sanity check
(`SavedGame.TerrainAgreement`). A mid-game save scores lower simply because the
dungeon has been dug, which is what the 73–85% figures are; it is not parser
error. **The peak is sharp**, measured across the fourteen earlier saves in the install:

| Read at | Agreement |
|---|---:|
| the located offset | **72.9% – 100%** |
| one tile record either side | 59.1% – 91.3% |
| **one byte** on | **0.000%**, each earlier save |

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
from record lanes 8 and 6-7 and needs no Unity-side change. Entities need the per-class
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

Q181 covers record bytes 2-3, the upper word bits, and the remaining bits of byte 9. Q183 covers the rest of the global
and player block; Q182 covers entity payloads. Q189's former 44+44
health anomaly was a shifted read, not an original-game effect.
