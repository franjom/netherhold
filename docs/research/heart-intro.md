# The Dungeon Heart's opening sequence

DKII.EXE v1.70, Ghidra primary reads and shipped Editor KWD records, 2026-10-14.
The room update (`0x004D9A60`, read whole twice) and the data are **CONFIRMED**;
of the chain mechanics the Next chain (`0x00440350`) is **CONFIRMED** and the
death path, the generation flags and the group parts are **LIKELY** (their records
name the unread steps); the sounds and lights are read but not built. Built as
ADR-0475.

The owner's recollection (2026-10-14): at the start of a level the player's Heart
"bursts" from its room tiles, creating the arches, and then starts beating; it is
the player's Heart, not necessarily the scripted or AI Hearts on the map. The
binary agrees on the first and qualifies the second: the plug and its burst are
asked for only for the local player's Heart, and only that Heart's Arches and
Big Steps are created invisible to fade in.

## 1. The room update's first branch (`0x004D9A60`)

The Dungeon Heart room's per-turn update keeps a start stamp at `room+0x5B`. A
stamp of 1 (what the builder leaves) becomes `TimeBeforeDungeonHeartConstructionBegins
× ticks per second + 1` (`0x004D9A77`); variable 218 ships as 0, so the sequence
starts on **game turn 1**. The ruined bit (`room+0x9A` bit 0) skips everything.
Then, with `now` the game turn and `start` the stamp:

| when | what |
|---|---|
| `now < start` and `start > 1` | request effect **223** (Heart Plug Static Lit) at the Heart's tile centre, z `0x1000`, with the room owner's tag (`0x004D9B6E`, slot `+0x38C`). Every Heart; never with the shipped delay |
| `start + 9 × tps < now` | the ordinary frame: regeneration once a second and the ruin of a room whose Heart object is gone (win-and-defeat.md §3). **Nothing regenerates or ruins before it** |
| elapsed 56..62 | the fog disc that never runs (Q430) |
| elapsed ≥ 7 × tps | every object of type 86 (Arches) and 88 (Big Steps) in the room's list gets alpha `+0x8F = min(255, (elapsed − 7 × tps) × 32)` and its tile is redrawn (`0x004522A0`); at exactly 9 × tps alpha 255 and `+0x9F` bit 0 cleared (`0x004D9CA0`) |
| elapsed 0, owner is the local player (`+0x264`) | request effect **225** (Start Level Effect) at the Heart's centre, z `0x1000`; sound `0xF1` through the owner's `+0x44E` (`0x004D9D2E`) |
| elapsed 7 × tps, local player | request effect **314** (Heart Arches fade in effect), same place (`0x004D9E3B`) |
| elapsed 9 × tps | the owner's `+0xA2` takes the turn; sound manager `+0x50(0xFF)` (`0x004D9EDA`) |

`tps` is variable 19, 4. The builder `0x004DB700` creates the local player's 86 and
88 with alpha 0 and `+0x9F` bit 0 set, so they are invisible until the ramp; another
keeper's are opaque from the start, and the ramp writes alpha on them too but their
bit 0 is clear.

## 2. The data

| effect | name | HP | makes | next | death | notes |
|---|---|---|---|---|---|---|
| 225 | Start Level Effect | 25 | element 406 each update | — | 224 | flags `0x8280` |
| 224 | plug light and static | 30 | 407 light overlay, 383 static; randomness 7 (one update in eight); height 560..720 | 223 | 208 | flags `0x8280` |
| 223 | Heart Plug Static Lit | 32 | 406 each update | — | — | flags `0x8480` |
| 208 | Heart Plug Explosion Effect | 1 | effects 205, 206, 207 (flag `0x40`) | — | — | light rgb 150,150,170 r 8192 at z 6144, fade-out 120 (flag `0x1000`) |
| 205 | Heart Plug data 0 | 1 | 379 plugbeam mesh, 403 Plugcracked mesh (HP 100 each) | — | — | |
| 206 | Heart Plug Data 1 | 2 | 25 a wave of 380 Prock mesh, 381 SM dust, 382 mist; origin 460..5865 | — | — | |
| 207 | Heart Plug Data 2 | 100 | 383 static each update, height 800 | — | 209 | |
| 209 | Heart plug data3 | 1 | effects 206, 210 | — | — | |
| 210 | Heart plug data4 | 10 | 8 a wave of 384 gsparkle (death 408); origin 345..2208 | 215 | — | light rgb 30,144,255 r 10240 at z 4096, fade-in 90 (flag `0x800`) |
| 215 | Plug Group Explosion | 1 | nothing a wave; **art `Group_plugbroke` (type 8)**: element 396 per member | — | — | |
| 314 | Heart Arches fade in effect | 1 | 128 of 525 gsparkle: vz 30720, mass 1638, friction 589, HP 48, ring 3657..3680 | — | — | |

Element 406 (Plug Element, uncracked) is the `DHeartPlug` mesh at scale 4096 with
HP 1: the lid is redrawn every update while 225 or 223 lives. `DHeartPlug` spans
2.28 tiles across and 0.27 tall about its origin, so at z `0x1000` it lies on the
ring floor over the pit. Element 396 (Plug Mesh Part): HP 100, vz 16384..22528,
XY speed −20480..0, mass 1638, friction 65, elasticity 1310, flag 1 (dies at the
floor). `Group_plugbroke` is nine members, `DHeartPlug1`..`DHeartPlug9`, each with a
three-float offset (KMF GROP: HEAD count, then ELEM name and offset).

## 3. How one instance begets another (second reader, 2026-10-14)

- **Next at creation.** `0x00440350` allocates the instance, pushes it on the
  view list `+0x336`, then loops over `definition+0x7B` (NextEffectId) creating
  each with the same tag, position and heading; only the first id passed the
  visibility gate `0x00440420` (flag `0x8000` skips its first test).
- **Color tag (rechecked 2026-10-16).** The225 request loads the room owner
  word at`+0x1D` (`0x004D9DBA`);`0x004347E0` stores the incoming tag at`+0x12D`.
  Generated elements receive that tag through`0x004352E0`; generated effects
  and death effects obtain it through the parent's virtual`+0xC` getter.
  No parent-flag`0x200` tag reset was found in these paths. The palette's
  special cycling-color branch remains scoped in room-construction-effects.md
  section6; a gray screenshot alone does not establish a neutral owner.
- **Death at removal.** The cleanup loop `0x0043C041` removes an instance only when
  its HP (`+0x10B`) is gone **and** its child list `+0x113` is empty; the remover
  `0x00440530` → `0x004354F0` creates `definition+0x7D` (DeathEffectId) at the
  instance's current position and would remove any children with argument 1 (no
  death element), which that order makes moot.
- **Generated effects.** With flag `0x40`, `0x004386F0` creates the generated ids
  as top-level instances through slot `+0x94` at the origin draw; they are not
  children. `GenerateRandomness` n lets a wave through once in n + 1 updates
  (`rand(n + 1) == 0`, before the HP test).
- **Group parts at birth.** `0x004347E0`, inside its art block (`(def+0xB) & 1`)
  and when the part count `def+0x8F` is nonzero (the loader `0x004358C0` fills the
  part array `+0x8B` from the resource when `def+0xA == 8`), spawns one element of
  the **first** generated id per member at the instance's position plus the
  member's offset, with no origin draw, adds `0x400` to its heading, draws its
  spin from the effect's spin range, and links it as a child. The member's mesh
  attached to the element (`0x004B37D0(record+0xC)`) and the offset's scale (the
  KMF float as tiles, z down) are INFERRED; ours gates on the art type 8.
- **FadeDuration** is the attached light's ramp (flags `0x800` in, `0x1000` out,
  `0x2000` in then out), not an alpha fade; not built.

With one quantum of 50 ms the shipped chain runs: the plug lid 0..1.25 s (225),
then light overlay, static and the lid again to about 2.9 s (224 with 223 beside
it), the explosion at about 2.85 s (208: beam, cracked plug, rocks, dust, static
for five seconds), and at about 7.9 s the static's death (209) throws rocks and
dust again, sparkles (210) and the nine plug pieces (215). The arches' ring of
sparkles and their fade-in run from 7.25 s to 9.25 s.

## 4. Built (ADR-0475)

- `HeartIntroRules` (simulation): the requests of §1 for every Heart, tagged with
  the owner; 225 and 314 are viewer-only, which the Unity runner filters by the
  viewing player. The ordinary frame waits for the sequence (`SequenceOver`).
- `WorldView.DecorateHeart`: the viewer's Arches and Big Steps carry
  `DecorationAlpha`; `ClassicMeshes.SetAlpha` hides at 0, blends between, restores
  at 255.
- `RoomConstructionParticles`: Next chains at creation, death effects at removal
  after the last child, flag `0x40` generation, the randomness gate, and group
  parts through an injected lookup; `KmfReader` reads GROP; `ClassicMeshes.GroupParts`
  maps a member's offset to position units (KMF x, y are the world's, z points
  down, a unit is a tile). `ClassicConstructionEffects` keys a mesh group by the
  part's entry.
- Not built: the two sounds, the lights of 208 and 210, effect 224's `+0x43`
  heading helper for generated effects (the element orientation draw stands in),
  the redraw of the arches' tile, and flag `0x8000`'s fog bypass (the Heart's own
  tile is always discovered by its owner).
- The beam. Element 379 (`plugbeam`, additive, flags `0xC`: it grows from scale 0.5
  toward 1.25 over its 100 updates and dims only once its HP is below its RGB of 25)
  is the white light the owner sees stand on the plug and vanish as the Heart
  appears. Its `Falloff` texture peaks at 68 of 255, so a plain additive draw is a
  quarter of DK2's blaze; the Unity renderer draws every additive effect-element
  mesh material, this beam's among them, at twice the texture (ADR-0476), a
  presentation choice until the additive texture stage is read.

- Turns, not ticks. `HeartIntroRules.Turn` maps our tick to DK2's game turn (tick 0
  is turn 1, as `GameSimulation` numbers triggers), so 225 is asked for on tick 0
  and the ordinary frame's wait ends after tick 36. That DK2 draws no frame before
  its first turn is STRONGLY INFERRED from the guard alone: it asks for the lid (223)
  on every turn before the start and by nothing else, and its render loop is unread.
  The Unity runner therefore runs the first tick, and one 50 ms update of the effect
  model (an emitter's first wave comes at its first update), before its first frame;
  a frame before either showed the open pit under no lid.

Open: a matched original comparison of the sequence's look and timing.
