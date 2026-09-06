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

## 8. What is implemented

Nothing.
