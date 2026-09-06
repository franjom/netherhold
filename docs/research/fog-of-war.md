# Fog of war: what a keeper has seen

**Source:** the owner playing DK2 v1.70 and one screenshot (2026-09-06), the
shipped `Terrain.kwd`, and `Text.str` read with this project's own
`Dk2StringTable`. **Status: newly opened, nothing implemented.** Until today
there was no research note and no open question about fog anywhere in the repo.

**Why it is worth opening now:** fog is unusually free of the dependencies that
block the rest of M4-M7 — no combat, no objectives, no room behaviour, no job
selection. What it needs is facts, and the first hour of looking produced more
of them than expected, including one that overturns the obvious model.

---

## 1. The substitution — CONFIRMED

**An unseen tile is presented as terrain id 27, and the game's own UI says so.**

The chain, end to end:

| Step | Value |
|---|---|
| `Terrain.kwd` record 27, debug name | `Fog Of War` |
| its flags | `0x0000000B` = `Solid, Impenetrable, Taggable`, health 1/1 |
| its `NameStringId` (record `+0x1A2`) | **2472** |
| `Text.str` id 2472 | **"Fog of War"** |
| its `TooltipStringId` (record `+0x1A0`) | **2473** |
| `Text.str` id 2473 | **"Fog of War: this area has not yet been explored."** |

And the owner's screenshot of the running game shows the caption bar reading
**"Fog of War"** with the cursor over blackness. So the tile under the cursor
resolves through terrain record 27 by the same path every other terrain does —
`Rock` is 2474/2475 and `Solid Rock` 2476/2477, in the same run of the table.

**DK2 does not hide a tile, it substitutes one.** Solid so it draws as a wall,
impenetrable so nothing invites digging, and taggable so a keeper can mark it
anyway. The screenshot shows the blue selection quad sitting on a fog tile in
open blackness, which is the taggable flag being honoured.

The six terrain types carrying `RevealThroughFogOfWar` (flag bit 24 —
Impenetrable Rock, Water, Lava, Gold, Gems, Edge of Map) are the exceptions that
show through. That flag is parsed into `TerrainFlags` and is **the only flag in
the enum with no `Is…` accessor and no reader**.

---

## 2. Fog is a game setting with modes — and this corrects the obvious model

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
confirmed because these are strings in a setup screen; nothing here has watched
the engine branch on them.

**This is the correction.** The owner's *"stays revealed"* is CONFIRMED and still
correct — and it is the behaviour named by option **351**, one of at least three
the engine ships. Writing "fog is monotonic, one bit per tile, no second layer"
as *the* model would have been this project's characteristic defect exactly:
internally consistent, contradicted by nothing available at the time, and wrong
about the engine while right about the campaign.

**What follows for the design:** persistence and reveal-source want to be **read
configuration**, not a hardcoded rule — which also serves Goal 2 (original
campaign content) and Goal 8 (modding). Where the setting is stored is unread.

---

## 3. What the owner has confirmed

Asked 2026-09-06.

- **CONFIRMED — fog does not close again**, in the campaign as played. *"Yes,
  stays revealed."* See §2 for the qualification the string table adds.
- **CONFIRMED — under fog you see nothing, drawn as a tile.** *"Black-ish but
  taggable tile terrain."* With the caveat that matters: there is **no guarantee
  a fog tile can be excavated until it is revealed** — marking it is a bet.
- **CONFIRMED — a fog tile can be tagged, and the tag may not survive the
  reveal.** *"It is rendered as a rock tile until it is revealed. If it is an
  impenetrable rock, or if it is a room that wasn't visible and was revealed,
  then its tag is lost."* So two cases discard a speculative mark: the revealed
  terrain is impenetrable, or it turns out to be a room.

That lands on a rule this project already has. `TileTagSystem` drops a tag on
non-taggable terrain rather than storing it
(`src/DK2.Simulation/Systems/TileTagSystem.cs`), and its comment already names
fog as taggable. **The same rule applied at reveal-time rather than mark-time is
the observed behaviour** — encouraging, and not the same as having checked it.
The room case is not obviously the same rule: room terrain is not `Taggable`, so
it would fall out of the same test, but that is an inference and not a reading.

- **HYPOTHESIS — the radius is about 6 tiles.** The owner's estimate from the
  screenshot's lit/black boundary, offered explicitly as needing independent
  confirmation. **Nothing in the shipped data supports or refutes it** — see §4.

---

## 4. The radius is not in the balance table — a checked negative

All 214 balance variables were searched. The only two radii are
`DungeonHeartClaimScanRadiusTiles` (15, a plain u8 tile count) and
`DefaultTorchLightRadiusTiles` (6144, u32).

**6144 is not 6 tiles.** `Fp32.One` is 4096, so it is **1.5 tiles** — and
`DefaultTorchLightHeightTiles` is the same 6144, which is a sensible height for a
wall torch and would be absurd as a sixth of the map. The project's own
convention corroborates the scale (`BoulderSpeedTilesPerSecond` 4915 = 1.2
tiles/s). This was checked precisely because 6144 and "about 6" looked like a
match, and it is not one.

So **if the reveal radius is 6, it is an immediate in the binary rather than a
tunable**, which is unusual for this game and is itself worth knowing.

---

## 5. A second partial-knowledge layer, distinct from fog

`Text.str` 2471 is the tooltip string of `Hero Stone Bridge` **and** reads:

> "This room is owned by another power. Its details are unknown."

A room you can *see* but do not own shows no details, where the owned-room
tooltip (2466) prints `Capacity used: %38 / %39. Creatures using this room: %65`.
That is a second visibility rule, operating on revealed ground, and it is not
fog.

Worth recording now because it constrains where fog lives: **if the presentation
layer has to withhold room details from a non-owner anyway, it needs a viewer
regardless**, and fog is not the only customer for one.

---

## 6. The architectural fact nobody has had to state

**The presentation boundary has no viewer.** `WorldView.TileAt(world, x, y)` takes
no player, `ScenePresenter.Present(WorldState)` takes no player, and `TileView`
carries `IsTaggedByAnyone` — so every renderer is omniscient by construction, and
both of them (`TextRenderer`, Unity's `DungeonScene`) rely on it.

That assumption is load-bearing and **written down nowhere**:
`docs/ARCHITECTURE.md` lists four decisions that live at the presentation boundary
and "whose view is this" is not among them.

The decision that follows: **does discovered-ness live in `WorldState` or beside
`WorldView`?** In `WorldState` it enters `HashInto`, `WorldSnapshot` and the
pinned cross-machine hash literal, and costs a `HashVersion` bump. That is only
correct if fog changes what the *simulation* does — which turns on whether a
computer keeper has fog of its own, and whether a tag on an undiscovered tile is
simulation state. **Not decided here; both are for the binary.**

---

## 7. Open questions

**Q190-Q194** in `docs/research/open-questions.md`, where the full status of each
is kept: fog's home (Q190), the radius and what reveals (Q191), whether the five
modes are real (Q192), the speculative tag (Q193), and the two reveal spells
(Q194). One more is unnumbered because Q66 already asks it of claiming: whether
the fog boundary takes in diagonal neighbours.

**Q190 has a cheap experiment, and it came from another mechanic.** The
saved-game work found three lanes of the 10-byte runtime tile — 1, 3 and 9 —
that move across most of the map between any two saves, recorded in
`sav-format.md` as *"consistent with visibility or fog state, and they are still
UNKNOWN"*. **If fog is in the save, it is game state rather than client state**,
because a save persists the simulation. Two saves either side of walking one
creature into unexplored ground would settle the architecture question without
opening Ghidra, using a method that is already built and validated by a control.

## 8. What is implemented — built 2026-09-06 (ADR-0076)

Built by Codex on `codex/fog-of-war` against Brief 8, **after an explicit owner
override of that brief's Phase 0 stop rule**. The stop rule said: if fog turns
out to be simulation state, report and stop. It is, and the owner authorised
continuing anyway, so it is in `WorldState` and cost a hash bump.

### 8.1 What the branch established, and how far each goes

- **Fog is simulation state — STRONGLY INFERRED.** The world-save coordinator
  invokes the runtime map's save operation; that operation walks every tile and
  writes ten contiguous bytes of the live 16-byte tile record, including the
  byte whose bit 6 gates fog rendering. **A save persists the simulation**, so
  the fog gate is not reconstructed by the renderer. That answers Q190's
  architecture half by the exact route `sav-format.md` predicted.
- **The reveal radius is six tiles — CONFIRMED from shipped data**, and
  independently re-derived on merge. `CreatureDef.PerceptionRange` (CREATURES
  offset `0x0D01`, already parsed before this work) is `0x6000` for **all 46**
  creatures, which at 4096 to the tile is exactly 6. **It is not
  `DistanceCanSee`**, which is `0x4000` — 4 tiles — for all 46, so the two are
  genuinely different fields and the right one was taken. This confirms the
  owner's visual estimate from a number rather than agreeing with it.
- **The footprint is a strict circle — STRONGLY INFERRED.** Squared fixed-point
  distance to tile centres, revealed only where it is **strictly less** than the
  range squared. Equality is outside.
- **`RevealThroughFogOfWar` is live — STRONGLY INFERRED.** Terrain flag bit 24
  is tested in the same decision as the tile fog gate, so it is not a dead table
  field. **Six terrains carry it**, re-derived on merge from the shipped data:
  Impenetrable Rock, Water, Lava, Gold, Gems and Edge of Map. Which is why an
  unexplored map still shows you where the gems are.
- **Fog is a boolean game setting — STRONGLY INFERRED.** The configuration
  reader and writer both handle a 32-bit `FogOfWar` value and every observed
  caller passes a single bit. **So the five setup labels of Q192 are NOT one
  enum behind this value**, and the branch does not pretend they are.

### 8.2 What is built

Per-keeper discovery on every tile, hashed and snapshotted. Each on-map,
non-dying, non-held creature reveals its data-defined circle for its owner after
movement, and discovery is **monotonic** — the campaign's *Stay Revealed*, which
is the one mode the owner has confirmed.

`WorldView` becomes keeper-specific: a hidden tile is substituted with the
shipped `Fog Of War` terrain and leaks no ownership, room furniture, health,
gold, creature or object. A keeper sees only its own speculative tag, and
terrain carrying `RevealThroughFogOfWar` bypasses substitution **through the
flag rather than an id list**.

**Fog defaults to OFF**, as a world and scenario setting, so nothing that
existed before this behaves differently.

**The speculative tag is revalidated at reveal-time**, which is the owner's Q193
observation reproduced by the rule that already existed: a mark on fog survives
only if the revealed terrain is itself `Taggable`, so room ground and
impenetrable rock discard it.

### 8.3 What is still UNKNOWN, in the branch's own list

- **Whether a computer keeper consults fog.** Persistence proves the state
  belongs to the simulation; it does not prove any AI reads it. Q190's second
  half is open.
- **The selectors behind `All Owned Reveal`, `Re-shroud` and `Re-fog`** (Q192).
  Not treated as one enum, because the configuration value is boolean.
- **Whether a creature in every uncommon state reveals.** The binary excludes
  the dying state; excluding a creature held in the Hand is ours.
- **How DK2 packs per-keeper discovery in multiplayer.** One bit per tile per
  keeper is our representation, not a claim about theirs.
- **Sight of Evil and Reveal Map** (Q194), deliberately absent. They can write
  through the same discovery seam when researched.
- **Whether the boundary takes in diagonal neighbours** — unnumbered, because
  Q66 asks the same of claiming.

## 9. Watched in play — CONFIRMED by the owner, 2026-09-06 (two screenshots)

Taken in a skirmish while Q190's second half was open. Two of §8's STRONGLY
INFERRED readings are now owner-confirmed, and one thing turned up that nothing
had predicted.

### 9.1 `RevealThroughFogOfWar` does exactly what it says — CONFIRMED

A **gem seam is visible inside unexplored blackness**, lit and rendered in full,
with no explored ground anywhere near it. Gems is one of the six terrains
carrying the flag (with Impenetrable Rock, Water, Lava, Gold and Edge of Map).

So the flag is not merely *live* — which is all the binary showed, by being
tested in the same decision as the tile fog gate — it produces the visible
behaviour its name claims. **This is why a keeper can plan for gold and gems
before exploring**, and it is a real gameplay affordance rather than a rendering
detail.

### 9.2 An undiscovered tile reports itself as terrain "Fog Of War" — CONFIRMED

Hovering an unexplored tile prints **`Fog of War`** in the status bar and in the
tile tooltip, exactly as any other terrain prints its own name. So the
substitution is the game's own model and not a presentation convenience of ours:
`WorldView` replacing a hidden tile with the shipped `Fog Of War` terrain
(ADR-0076) is what DK2 is doing too.

**And the tile is markable**: the screenshot has the dig cursor over it with the
selection outline drawn. That is Q193's premise seen directly.

### 9.3 The minimap shows only the viewer's own exploration — CONFIRMED

*"It shows only what my creatures have explored."* Which settles that fog is
**not a union** across keepers — you never see a rival's sight line.

**Note carefully what this does NOT settle.** It is a statement about the
viewer's own view, and all three candidate architectures produce it: shared fog
is ruled out, but *per-keeper fog* and *one viewpoint plus an omniscient AI* both
look exactly like this from the keeper's chair. Q190's second half is untouched
by it.

### 9.4 Two portals show through the fog, dimmed — and nothing explains this yet

The owner's minimap carries **two portals in a darkened tint**, on ground no
creature of theirs has explored.

**This cannot be `RevealThroughFogOfWar`.** That is a *terrain* flag and a Portal
is a **room** (type 3). Nothing read so far accounts for a room being visible
through fog, and the minimap colour routine that was read (`FUN_00451c90`) tests
exactly one room type in its fogged branch and it is the **Dungeon Heart**
(type 5), not the Portal.

So there is a second mechanism. The dimming suggests an *overlay* — icons drawn
on top of the minimap for strategically essential rooms — rather than a tile
colour, but that is a hypothesis and not a reading. **Q197.**

## 10. The multiplayer argument, and why it points the other way

The owner's objection (2026-09-06) is the strongest one available and deserves
answering in full:

> *"I can accept that an AI keeper can see the entire map, but what about a
> multiplayer match between two human players? They can't have shared fog of
> war, it must be per keeper."*

**Two humans certainly cannot share fog.** But that does not require a
per-keeper mask in the tile, because of where the copies live.

DK2's tile record carries **one** fog bit — `tile + 0xB`, bit `0x40`, a
hardcoded bit with no player index anywhere in the expression, read that way by
the map renderer in at least two routines. In a peer-to-peer game each machine
runs its own copy of the world, and that one bit means *"fogged for the player
sitting at this machine"*. Player A's machine holds A's fog in it; player B's
machine holds B's fog in the same byte. Neither ever sees the other's, and no
per-keeper mask exists anywhere. **Two humans, no shared fog, one bit.**

So the observation is satisfied by both architectures and separates neither.

### 10.1 And now the argument reverses

If each machine's fog bit holds a *different* value — which is exactly what
"per-machine local fog" means — then **any simulation logic that read that bit
would produce a different result on each machine.** In a lockstep game that is a
desync. So under this reading, the fact that DK2's multiplayer works at all is
evidence that **nothing in the simulation reads fog**.

That inverts the intuition the objection starts from: multiplayer does not argue
for fog being simulation state, it argues against fog being *consulted* by the
simulation. Q190's two halves come apart here as sharply as anywhere.

**The argument rests on an assumption worth naming**: that DK2's multiplayer is
deterministic lockstep rather than state-synchronised from an authority. That is
the norm for RTS of the period and it is **not established here**. If DK2 syncs
state instead, the argument does not run.

### 10.2 The experiment that settles it, using machinery that already exists

The owner's own save is enough, and `Dk2SaveReader` plus the lane analysis in
`sav-format.md` is already built and validated by a control:

1. Play a skirmish far enough to explore a **distinctively shaped** region, and
   note it off the minimap — which §9.3 confirms shows exactly the viewer's own
   exploration.
2. Save.
3. Dump lanes **1, 3 and 9** bit by bit as a per-tile bitmap.
4. Find the bit whose set-region matches the explored shape. That is fog, and it
   identifies which of the three unknown lanes carries it — worth having on its
   own.
5. **Then the actual question:** is any *other* bit in those lanes set over a
   *different* region — one belonging to another keeper's exploration?

**Yes → per-keeper discovery, stored, and Q190's second half is answerable.
No → one viewpoint, and this project's per-keeper mask is ours alone.**

Either way it also closes the three UNKNOWN lanes that `sav-format.md` has been
carrying since the format work began.

