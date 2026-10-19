# Fog of war: what a keeper has seen

| | |
|---|---|
| Source(s) | The owner playing DK2 v1.70 with three screenshots and a three-save series (2026-09-06); shipped `Terrain.kwd` and `CREATURES`; `Text.str` read with this project's `Dk2StringTable`; `DKII.EXE` v1.70 (the map renderer's fog gate, the world-save coordinator, `FUN_00451c90`) |
| Confidence | **CONFIRMED**: the terrain substitution, the six-tile radius, that reveal-through-fog terrain shows through, monotonic discovery, and one bit for one viewpoint. **STRONGLY INFERRED**: that fog is simulation state, the strict-circle footprint, that the setting is a boolean. **UNKNOWN**: whether any AI consults fog, what the five setup labels select, RevealMap, the minimap's portals |
| Implementation | `Systems/FogOfWarSystem`, `TileState.DiscoveredBy` / `IsDiscoveredBy` / `SetDiscoveredBy`, `WorldState.FogOfWarEnabled`, `TerrainDef.IsRevealedThroughFog`, `WorldView` (constructed with a viewing player). ADR-0076 |
| Tests | `FogOfWarTests`, `FogOfWarOnRealDataTests`, `FogOfWarViewTests`; `Dk2SaveReader` for the save-side evidence |
| Open questions | Q190's AI half, Q192 (the setup labels), Q193's mechanism, Q194 (Reveal Map; Sight answered in `keeper-spell-sight.md`), Q197 (portals on the minimap) |
| Reveal sources | Every writer of both discovery bits, and the second, *explored* bit this file's §11 missed: `fog-reveal-sources.md` (Q429, Q430) |

## 1. The substitution — CONFIRMED

**An unseen tile is presented as terrain id 27, and the game's own UI says so.**

| step | value |
|---|---|
| `Terrain.kwd` record 27, debug name | `Fog Of War` |
| its flags | `0x0000000B` = `Solid, Impenetrable, Taggable`, health 1/1 |
| its `NameStringId` (record `+0x1A2`) | **2472** |
| `Text.str` id 2472 | **"Fog of War"** |
| its `TooltipStringId` (record `+0x1A0`) | **2473** |
| `Text.str` id 2473 | **"Fog of War: this area has not yet been explored."** |

The owner's screenshot shows the caption bar reading **"Fog of War"** with the
cursor over blackness, so the tile under the cursor resolves through terrain
record 27 by the same path every other terrain does — `Rock` is 2474/2475 and
`Solid Rock` 2476/2477, in the same run of the table.

**DK2 does not hide a tile, it substitutes one.** Solid so it draws as a wall,
impenetrable so nothing invites digging, and taggable so a keeper can mark it
anyway. The screenshot shows the blue selection quad sitting on a fog tile in
open blackness, which is the taggable flag being honoured.

Six terrain types carry `RevealThroughFogOfWar` (flag bit 24): Impenetrable
Rock, Water, Lava, Gold, Gems and Edge of Map. They show through — see §9.1.

## 2. Fog is a game setting with modes

`Text.str` ids 344-355 are a contiguous block of skirmish/multiplayer setup
labels — `Force Possession`, `Start Location`, `Fixed`, `Random`, then:

```
348  Fog of War
349  Creatures Reveal
350  All Owned Reveal
351  Stay Revealed
352  Re-shroud
353  Re-fog
354  Cheats
```

**STRONGLY INFERRED**: 348 is a group label and 349-353 are its options — two
reveal *sources* (`Creatures Reveal`, `All Owned Reveal`) and three persistence
behaviours (`Stay Revealed`, `Re-shroud`, `Re-fog`). Inferred rather than
confirmed because these are strings in a setup screen; nothing has watched the
engine branch on them, and §8.1 found the configuration value to be a single bit
rather than an enum. **Q192.**

The owner's *"stays revealed"* is CONFIRMED and is the behaviour named by option
**351**. Writing "fog is monotonic, one bit per tile, no second layer" as *the*
model would have been internally consistent, contradicted by nothing available at
the time, and wrong about the engine while right about the campaign. Persistence
and reveal-source therefore want to be **read configuration** rather than a
hardcoded rule, which also serves Goal 2 and Goal 8. Where the setting is stored
is unread.

## 3. What the owner has confirmed

Asked 2026-09-06.

- **Fog does not close again**, in the campaign as played: *"Yes, stays
  revealed."*
- **Under fog you see nothing, drawn as a tile**: *"Black-ish but taggable tile
  terrain."* With the caveat that matters — there is **no guarantee a fog tile
  can be excavated until it is revealed**, so marking it is a bet.
- **A fog tile can be tagged, and the tag may not survive the reveal**: *"It is
  rendered as a rock tile until it is revealed. If it is an impenetrable rock,
  or if it is a room that wasn't visible and was revealed, then its tag is
  lost."* Two cases discard a speculative mark: the revealed terrain is
  impenetrable, or it turns out to be a room.

That lands on a rule this project already had. `TileTagSystem` drops a tag on
non-taggable terrain rather than storing it, and **the same rule applied at
reveal-time rather than mark-time is the observed behaviour** (§8.2). The room
case is an inference rather than a reading: room terrain is not `Taggable`, so it
falls out of the same test.

## 4. The radius is not in the balance table — a checked negative

All 214 balance variables were searched. The only two radii are
`DungeonHeartClaimScanRadiusTiles` (15, a plain u8 tile count) and
`DefaultTorchLightRadiusTiles` (6144, u32).

**6144 is not 6 tiles.** `Fp32.One` is 4096, so it is **1.5 tiles** — and
`DefaultTorchLightHeightTiles` is the same 6144, a sensible height for a wall
torch and absurd as a sixth of the map. The project's own convention corroborates
the scale (`BoulderSpeedTilesPerSecond` 4915 = 1.2 tiles/s). Checked precisely
because 6144 and "about 6" looked like a match, and it is not one. The radius is
in CREATURES instead — §8.1.

## 5. A second partial-knowledge layer, distinct from fog

`Text.str` 2471 is the tooltip string of `Hero Stone Bridge` **and** reads:

> "This room is owned by another power. Its details are unknown."

A room you can *see* but do not own shows no details, where the owned-room
tooltip (2466) prints `Capacity used: %38 / %39. Creatures using this room: %65`.
That is a second visibility rule, operating on revealed ground, and it is not
fog. Worth recording because it constrains where fog lives: **if the presentation
layer has to withhold room details from a non-owner anyway, it needs a viewer
regardless**, and fog is not the only customer for one.

## 6. The presentation boundary has a viewer

It did not before this work: `WorldView.TileAt` took no player and `TileView`
carried `IsTaggedByAnyone`, so every renderer was omniscient by construction and
both of them (`TextRenderer`, Unity's `DungeonScene`) relied on it. `WorldView`
now takes a viewing player id at construction and every tile it hands out is that
keeper's view.

The decision that followed — **does discovered-ness live in `WorldState` or
beside `WorldView`?** — went to `WorldState`, so it enters `HashInto`,
`WorldSnapshot` and the pinned cross-machine hash literal and cost a
`HashVersion` bump (§8).

## 7. Open questions

**Q190-Q194** in `docs/research/open-questions.md` keep the full status of each:
fog's home (Q190, answered in §11), the radius and what reveals (Q191, answered
and built), whether the five modes are real (Q192), the speculative tag (Q193,
outcome reproduced, mechanism unknown) and the two reveal spells (Q194). Q197 is
§9.4. The reveal footprint is a circle rather than a neighbour walk, so Q66's
diagonal question does not arise for fog.

## 8. What is implemented — built 2026-09-06 (ADR-0076)

Built by Codex on `codex/fog-of-war` against Brief 8, **after an explicit owner
override of that brief's Phase 0 stop rule**. The stop rule said: if fog turns
out to be simulation state, report and stop. It is, and the owner authorised
continuing anyway.

### 8.1 What the branch established, and how far each goes

- **Fog is simulation state — STRONGLY INFERRED.** The world-save coordinator
  invokes the runtime map's save operation; that operation walks every tile and
  writes ten contiguous bytes of the live 16-byte tile record, including the byte
  whose bit 6 gates fog rendering. **A save persists the simulation**, so the fog
  gate is not reconstructed by the renderer. That answered Q190's architecture
  half by the exact route `sav-format.md` predicted.
- **The reveal radius is six tiles — CONFIRMED from shipped data**, and
  independently re-derived on merge. `CreatureDef.PerceptionRange` (CREATURES
  offset `0x0D01`, parsed before this work) is `0x6000` for **all 46** creatures,
  which at 4096 to the tile is exactly 6. **It is not `DistanceCanSee`**, which
  is `0x4000` — 4 tiles — for all 46, so the two are genuinely different fields
  and the right one was taken. This confirms the owner's visual estimate from a
  number rather than agreeing with it.
- **The footprint is a strict circle — STRONGLY INFERRED.** Squared fixed-point
  distance to tile centres, revealed only where it is **strictly less** than the
  range squared. Equality is outside.
- **`RevealThroughFogOfWar` is live — STRONGLY INFERRED.** Terrain flag bit 24 is
  tested in the same decision as the tile fog gate, so it is not a dead table
  field. Six terrains carry it, re-derived on merge from the shipped data. Which
  is why an unexplored map still shows you where the gems are.
- **Fog is a boolean game setting — STRONGLY INFERRED.** The configuration reader
  and writer both handle a 32-bit `FogOfWar` value and every observed caller
  passes a single bit. **So the five setup labels of Q192 are NOT one enum behind
  this value**, and the branch does not pretend they are.

### 8.2 What is built

Per-keeper discovery on every tile (`TileState.DiscoveredBy`), hashed and
snapshotted. Each on-map, non-dying, non-held creature reveals its data-defined
circle for its owner after movement, and discovery is **monotonic** — the
campaign's *Stay Revealed*, the one mode the owner has confirmed.

`WorldView` is keeper-specific: a hidden tile is substituted with the shipped
`Fog Of War` terrain and leaks no ownership, room furniture, health, gold,
creature or object. A keeper sees only its own speculative tag, and terrain
carrying `RevealThroughFogOfWar` bypasses substitution **through the flag rather
than an id list** (`TerrainDef.IsRevealedThroughFog`).

**Fog defaults to OFF**, as a world and scenario setting, so nothing that existed
before this behaves differently.

**The speculative tag is revalidated at reveal-time**, which is Q193's
observation reproduced by the rule that already existed: a mark on fog survives
only if the revealed terrain is itself `Taggable`, so room ground and
impenetrable rock discard it.

### 8.3 What is still UNKNOWN, in the branch's own list

- **Whether a computer keeper consults fog.** Persistence proves the state
  belongs to the simulation; it does not prove any AI reads it. Q190's second
  half — see §11.4 for how far the save data takes it.
- **The selectors behind `All Owned Reveal`, `Re-shroud` and `Re-fog`** (Q192).
  Not treated as one enum, because the configuration value is boolean.
- **Whether a creature in every uncommon state reveals.** The binary excludes the
  dying state; excluding a creature held in the Hand is ours.
- **How DK2 packs per-keeper discovery in multiplayer.** One bit per tile per
  keeper is our representation — and §11 shows DK2 keeps one bit for one
  viewpoint instead.
- **Sight of Evil** is a separate presentation region, unbuilt; its cast pays
  and stamps its clocks (ADR-0352, `keeper-spell-sight.md` §4). **Reveal Map** remains Q194.

## 9. Watched in play — CONFIRMED by the owner, 2026-09-06 (two screenshots)

Taken in a skirmish while Q190's second half was open.

### 9.1 `RevealThroughFogOfWar` does exactly what it says — CONFIRMED

A **gem seam is visible inside unexplored blackness**, lit and rendered in full,
with no explored ground anywhere near it. So the flag is not merely *live* —
which is all the binary showed, by being tested in the same decision as the tile
fog gate — it produces the visible behaviour its name claims. **This is why a
keeper can plan for gold and gems before exploring**, and it is a gameplay
affordance rather than a rendering detail.

### 9.2 An undiscovered tile reports itself as terrain "Fog Of War" — CONFIRMED

Hovering an unexplored tile prints **`Fog of War`** in the status bar and in the
tile tooltip, exactly as any other terrain prints its own name. So the
substitution is the game's own model and not a presentation convenience of ours.
**And the tile is markable**: the screenshot has the dig cursor over it with the
selection outline drawn, which is Q193's premise seen directly.

### 9.3 The minimap shows only the viewer's own exploration — CONFIRMED

*"It shows only what my creatures have explored."* Which settles that fog is
**not a union** across keepers — you never see a rival's sight line.

**Note what this does NOT settle.** It is a statement about the viewer's own
view, and all three candidate architectures produce it: shared fog is ruled out,
but *per-keeper fog* and *one viewpoint plus an omniscient AI* both look exactly
like this from the keeper's chair.

### 9.4 Two portals show through the fog, dimmed

The owner's minimap carries **two portals in a darkened tint**, on ground no
creature of theirs has explored. **This cannot be `RevealThroughFogOfWar`**: that
is a *terrain* flag and a Portal is a **room** (type 3). Nothing read so far
accounts for a room being visible through fog, and the minimap colour routine
that was read (`FUN_00451c90`) tests exactly one room type, the **Dungeon Heart**
(type 5), and only after its fog test, so no room, the Heart included, is drawn
through fog by it (full body reread 2026-10-17). So there is a second
mechanism; the dimming suggests an *overlay* — icons drawn on top of the minimap
for strategically essential rooms — rather than a tile colour, which is a
hypothesis and not a reading. **Q197.**

**The same day's TrapTime saves narrow it (read 2026-10-19).** At the true
tile alignment (`fog-reveal-sources.md` §2), `fow_1..3.SAV` hold three 3×3
Portals (terrain 12) at x 15–17, 54–56 and 92–94, y 33–35. All are owner 2 in
saves 1 and 2; in save 3 player 4 owns the left one, above its own Heart. None
of their tiles is explored in any save. The middle one, above player 3's
Heart, is fully scanned; the other two are neither explored nor scanned.
Terrain 12 is not one of the six `RevealThroughFogOfWar` terrains. Every
direct-call writer of the minimap class grid either calls `0x00451C90`
(`0x00451C60`, `0x004519F0`, `0x00451A80`, `0x00451BE0`, `Tile_SetTerrain`,
`Map_RefreshTile_LIKELY`) or writes the tag class `0x0D`. The grid pointer is
also returned by world getter `0x00508C70`, whose virtual callers are unread,
so a writer through it is not excluded. The level start's
whole-map rebuild (`0x0050C058`) runs after the fog seed (`0x0050B5D5`), so
no class from before the seed survives. **If the screenshot comes from the
session these saves record, nothing in the class grid explains the Portals.**
That the screenshot and the saves are the same game is an assumption: the
screenshot is not in the repository, and which two Portals it shows is not
recorded.

## 10. The multiplayer argument, and why it points the other way

The owner's objection (2026-09-06):

> *"I can accept that an AI keeper can see the entire map, but what about a
> multiplayer match between two human players? They can't have shared fog of
> war, it must be per keeper."*

**Two humans certainly cannot share fog.** But that does not require a per-keeper
mask in the tile, because of where the copies live. DK2's tile record carries
**one** fog bit — `tile + 0xB`, bit `0x40`, a hardcoded bit with no player index
anywhere in the expression, read that way by the map renderer in at least two
routines. In a peer-to-peer game each machine runs its own copy of the world, and
that one bit means *"fogged for the player sitting at this machine"*. Player A's
machine holds A's fog in it; player B's machine holds B's fog in the same byte.
Neither ever sees the other's. **Two humans, no shared fog, one bit** — so the
observation is satisfied by both architectures and separates neither.

### 10.1 And now the argument reverses

If each machine's fog bit holds a *different* value — which is exactly what
"per-machine local fog" means — then **any simulation logic that read that bit
would produce a different result on each machine.** In a lockstep game that is a
desync. So under this reading, the fact that DK2's multiplayer works at all is
evidence that **nothing in the simulation reads fog.** Multiplayer does not argue
for fog being simulation state; it argues against fog being *consulted* by the
simulation, and Q190's two halves come apart here as sharply as anywhere.

**The argument rests on an assumption worth naming**: that DK2's multiplayer is
deterministic lockstep rather than state-synchronised from an authority. That is
the norm for RTS of the period and it is **not established here**. If DK2 syncs
state instead, the argument does not run.

### 10.2 The method that settled it

`Dk2SaveReader` plus the lane analysis in `sav-format.md`, both already built and
validated by a control: explore a distinctively shaped region, save, dump lanes
**1, 3 and 9** bit by bit as a per-tile bitmap, and find the bit whose set-region
matches the explored shape. Then ask whether any *other* bit in those lanes is
set over a *different* region — one belonging to another keeper's exploration.
§11 is the run.

## 11. Q190 answered from the save data — ONE bit, ONE viewpoint

The owner made a three-save series on **TrapTime**, a 110x80 skirmish map with
four keepers on it.

### 11.1 The fog bit is record lane 9, bit 6

Diffing all **80 bit positions** of the 10-byte save tile record across the three
saves, exactly one bit has the fingerprint §10.2 predicted: quiet in the control,
loud in the signal, and **changing over a band far wider than the tunnel that
caused it**.

| | |
|---|---:|
| Tiles where it changed, control (nothing done) | 36 |
| Tiles where it changed, signal (one tunnel, 2m30s) | 306 |
| Shape of the changed set | **one** component, 26 wide x **13 tall** |
| Tunnel that caused it | 25 tiles long, **1 tall**, centred in that band |

Thirteen tiles tall, centred on the tunnel row, is a **six-tile radius** — the
`PerceptionRange` of `0x6000` that all 46 creature records carry (§8.1),
arriving by a completely independent route.

### 11.2 It belongs to exactly one keeper, and there is no second one

The save's owner lane gives four territories. Record bit 9.6 in the final save:

| keeper | tiles owned | bit 9.6 set |
|---|---:|---:|
| **player 3** (the owner's own) | 81 | **100%** |
| player 1 | 412 | **0%** |
| player 4 | 104 | **0%** |
| player 5 | 49 | **0%** |

**All 80 bit positions were then checked for a mirror** — a bit set over another
keeper's territory the way 9.6 is set over player 3's. There is none. The only
other bits that discriminate between territories are record lane 6's bits 0, 1 and
2, which *are* the owner id: player 3 is `0b011`, player 4 `0b100`, player 5
`0b101`, and the table reproduces exactly that. **That the owner id falls out of
the same scan is what makes the negative result trustworthy** — the method can
find a per-territory bit when one exists.

### 11.3 Monotonic, with zero reversals

Across all three saves, **342 tiles went 0 to 1 and not one went 1 to 0.** So
*Stay Revealed* is confirmed from the data as well as from play, and DK2's
campaign fog is an accumulate-only discovery set.

### 11.4 What this settles, and what it does not

**Settled: DK2 does not store per-keeper fog.** The save carries one discovery
bit and it is the local player's. This is the same conclusion the binary reached
from the other end — the renderer's gate is `tile + 0xB` bit `0x40`, a hardcoded
bit with no player index — and the two routes agree on the **bit number** as well
as the architecture, which is worth more than either alone. So the multiplayer
objection resolves as §10 predicted: per-machine, not per-keeper.

**Strong, not absolute, on the AI half.** An AI keeper's fog could in principle
live in memory and never be saved — but then loading a game would hand every
computer keeper a blank or omniscient slate, and its behaviour would visibly
change across a save/load. Q190's second half is answered to the standard of *"no
evidence for, and the storage that would be needed is absent"*.

**What is ours, restated.** ADR-0076's per-keeper discovery mask is this
project's representation. It is worth keeping — ours is derived deterministically
from creature positions, so it is per-keeper for free and cannot diverge — but it
should not be described as DK2's model, because DK2 tracks one viewpoint.

### 11.5 Two corrections to the save format

**The tile grid is NOT at a fixed `0x8861`.** In these saves the record start is at
**`0x14878`** at the true record start. `sav-format.md` recorded the offset as fixed on the strength of two
saves *of the same level*: two samples of one thing are one sample. The grid is
found reliably by correlating record lane 8 against the level's design map — 99.7%
agreement on a save taken at the start, a far sharper signal than the 73-85% the
original series reported from mid-game saves.

**Record lane 9 bit 6 is discovery**, correcting the earlier terrain-anchored lane numbering. The old *"lanes 1, 3 and 9 move
across most of the map between any two saves and are still UNKNOWN"* entry. They
churn because they were read as whole bytes; **bit by bit they are quiet**, and
the churn is in their other bits. The former lane 3 high nibble is a separate matter: it
resets to zero from many different values wherever *any* keeper is active, in
both territories at once, so whatever it is, it is a property of the tile and not
of a viewer. Still UNKNOWN, and no longer a fog candidate.
