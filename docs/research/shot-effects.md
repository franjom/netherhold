# Shot and spell visual effects: trails, timed and hit effects, the Inferno, procedural lightning

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 (pyghidra-mcp) on 2026-10-26. Facts in our
  own words (ADR-0046). Four parallel reads, each spot-checked in the listing afterwards; the
  checked instructions are cited.
- The reference install's `Shots.kwd`, `Effects.kwd` and `EffectElements.kwd`, read with
  `DK2.Data`'s layouts.

Original version: DK2 v1.70 (GOG)

Questions: opens Q658–Q662. [shots.md](shots.md) is the shot's simulation; this page is what it
shows. The effect engine itself (type-4 emission, element motion, the 50 ms view clock) is
[room-construction-effects.md](room-construction-effects.md).

**What this settles:**
- A shot shows four things: a **trail** effect it holds for its whole life, fire-and-forget
  effects at **creation, timed, hit-solid, hit-thing and death**, its own **body** art, and for
  art type 7 a **procedural lightning bolt** rebuilt every rendered frame.
- None of these draws on the simulation's random stream. **Creature burning does:** nine draws
  per burning creature per tick (§5).
- DK2's Inferno is a real fire carpet. Shot 15's creation effect 107 carries flag `0x40`, so
  its one id is **effect 106**, a 112-update field of fire sprites with a light. It is not
  element 106.

---

## 1. How the simulation asks for an effect

The world vtable `0x0066E3EC` has four effect slots (CONFIRMED):

| slot | function | does |
|---|---|---|
| `+0x388` | `0x0050E490` | → view `+0x90` `0x00440420`: view state, map bounds, solidity and visibility gates (definition flag `0x8000` skips the camera test), then view `+0x98` |
| `+0x38C` | `0x00509580` | copies the position into the second point, then `+0x388` |
| `+0x390` | `0x0050E4D0` | → view `+0x98` `0x00440350` directly, **no gate**; makes the whole `+0x7B` Next chain, returns the first |
| `+0x394` | `0x0050E510` | → view `+0x9C` `0x00440530`: removes an effect at once (its death effect plays, its elements are dropped) |

The effect object (vtable `0x0066CBB4`): slot `+8` `0x00434780` is Kill (HP := 0). Its second
point is `+0x117`. Its `+0x145` says whether its lifetime counts down: the initialiser
`0x004347E0` sets 1.

A gated request that fails is not retried. The simulation keeps a handle only for the trail
and the light (§2). It writes into them (position, second point, carried velocity, Kill) and
never reads them back. A shot's save record (`0x004AB090`, `0x004AB120`) streams `+0x124` and
`+0x12D..+0x172`, so neither handle is saved. The view effects draw on the presentation seed
`0x006D6450`, and the shot paths draw no random number (CONFIRMED).

## 2. A shot's effects, event by event

| event | definition field | request | where, heading | held? |
|---|---|---|---|---|
| shot made (`0x004AAC90`, `0x004AAE79..0x004AAED0`) | `+0x6D` general (trail) | `+0x390`, ungated | spawn point, init heading | yes, `+0x12B` |
| shot made | `+0x1B` light | view `+0xA0` `0x00439880` | position + light offset | yes, `+0x129`; never made in shipped data (descriptor `+0x10` bit 0 clear everywhere) |
| first tick (`0x004AB1AA`) | `+0x59` creation | `+0x38C` | spawn point, `+0xF4` | no |
| age `% TimedDelay == 0` (`0x004AB6F0`) | `+0x6F` timed, `+0x71` delay | `+0x388`, second point = aim `+0x131` | position after the move | no |
| physics returns 0 (`0x004AB343`): a wall, or a shot too slow or standing | `+0x5E` hit-solid | `+0x38C` | the start-of-tick point (physics does not move a stopped shot) | no |
| thing struck (`0x004ABEDC..0x004ABEFE`) | `+0x67` hit-thing | `+0x38C` | shot position at collision | tied to a creature (below) |
| health below 1 (`0x004AB515`) | `+0x5B` death | `+0x38C` | position after the move | no |

**The trail** (CONFIRMED):
- **Set up at shot creation.** The trail is made when the shot is made, before launch. It is
  tied to the shot's tag (effect `+0x133`, `+0x135` = 0) and gets `+0x145 = 0`, so **its HP
  never counts down**: it emits every update while the shot lives, whatever its authored HP
  (48 for most trails).
- **Carried velocity.** Launch (`0x004AB8A0` → `0x004ABA10`) writes the effect's carried
  velocity `+0x149..+0x151` = shot velocity × the world's tick rate / **20** and sets `+0x155`
  to 1. The generators add it to each new child when the effect has flag `0x100` (§3). Most
  shipped trails have `0x100`, so their particles leave with the shot's speed; Alarm 273, Fear
  239 and Cyclone 153 do not, so theirs start at rest. Only the launch
  writes it; the Guided Bolt's re-aims do not refresh it.
- **Aim.** `0x004AB830` stores the aim `+0x131` and pushes it into the trail's second point.
- **Following the shot.** A shot is thing class 1 (`0x004AAC90` passes 1 down through
  `0x004B1DF0` and `0x004B28D0` to `0x004B4A10`, which stores `+0x12`). `0x00434E80`'s class-1 arm places the effect at the shot's
  render-interpolated point (`0x0044CE70`) every view update, when `0x00447A70` accepts that
  point (in bounds, not solid), and moves **only the effect**:
  elements already born stay where they are. The creature arm moves them too. The tick's tail
  (`0x004AB745..0x004AB7A4`) also sets it to the start-of-tick point `+0xF6`. The heading stays
  the launch heading.
- **End.** Teardown `0x004AAF70` runs in the world tick the shot dies (`0x004B7830`'s second
  pass). If the effect's `+0xF4` is 0, it detaches the trail and Kills it. Emission stops at
  once. The live particles finish their own lives, and the effect and its light stay at the
  last point until they do. Then `0x004354F0` requests the trail's death effect (Fireball
  trail 64 → 50) through the gated view slot `+0x90`. Shot `Flags` bit 6 would remove the
  trail at once through `+0x394`; no shipped shot has it.

**Creation effect (CONFIRMED).**
- With shot `Flags` bit 7 (`0x004AB1E0..0x004AB260`), the creation effect is given the same
  carried velocity: velocity × `0x006F2590` (variable 19) / **20**, and `+0x155` = 1.
  `+0x155` is a field of the effect, not the shot's max range.
- The shot keeps no handle to it, so it does not follow the shot. No shipped shot has bit 7.

**Timed effect.** The id is tested before the division, so `TimedDelay` 251 with effect 0
never divides. Only Raise Dead (shot 40) ships one: effect 286, every tick.

**Hit-thing (CONFIRMED, `0x004ABEEF..0x004ABEFE`).**
- When the struck thing is a creature (class 0), the creature's tag is written into the
  **effect's** `+0x133`.
- The existing creature attachment arm then carries the effect and its elements with the
  creature.
- The creature's own fields are not written.

**Death timing.**
- A max-range kill and a die-on-hit-thing kill only zero health, so the death effect and the
  death shot still play in that tick.
- A die-on-solid kill (bit 0) returns early: it shows the hit-solid effect and no death effect.

**Dead fields.** Hit-lava `+0x61` and hit-water `+0x64` are loaded (`0x004AD024`, `0x004AD046`).
An instruction search for word reads at those offsets found none in the shot and physics
code, and physics has no lava/water arm for shots (STRONGLY INFERRED; the rest of the
executable was not walked for them).

**Body.**
- The definition itself is the art: `0x004AAF60` → `0x004B37D0` on `+0x44`, advanced each tick
  by `0x004B3AD0`.
- It is drawn by `0x00599C10` → `0x0059B880`, by art type:
  - 1–3 sprite: `0x0059C360` (flat `0x0059C720`);
  - 4 terrain mesh: `0x0059D010`;
  - 5 mesh: `0x0059CCF0`;
  - 6 animated mesh: `0x0059C880`;
  - 7 procedural: `0x0059C2A0` → `0x005A01A0` (§6).
- For effects the runtime art flags are the file flags OR 1 (`0x004347E0`), and bit 0,
  tested at `0x0059B910`, means loaded. That shot art is loaded the same way is STRONGLY
  INFERRED. `0x0059B880` has further gates: render-info `+0xAF` bit 0 (`0x0059B919`), and art
  flag `0x40000` against the global `0x00759ADC` (`0x0059B8A1`).
- Only the yaw is stored, so bodies draw without pitch (STRONGLY INFERRED).
- No shipped shot has a light: the glow is the trail's own effect light.

## 3. What the engine does that shot effects need

Effect definition runtime offsets are in [room-construction-effects.md](room-construction-effects.md).
These are the pieces that the shot effects exercise:

| piece | behaviour | address | confidence |
|---|---|---|---|
| effect flag `0x40` | generated ids are **effects**, made through view `+0x94` (`0x0043895D`); without it, elements through `0x004352E0` (`0x00438DFA`, `0x00438FA9`). Shot users: 107 Inferno → 106, 153 Wind → 152/154/155, 245, 284 → 4, 362 Armour Hit → 361/363 | `0x004386F0` | CONFIRMED |
| effect flag `0x100` | each new child's velocity += the carried velocity `+0x149` (+ the effect's own) | `0x00438A58..0x00438A94` | CONFIRMED |
| DirectionalSpread `+0x43` | nonzero: the element's velocity heading is its heading + `Random(8s) − 4s`, not uniform. ds 1 a jet along the launch heading (77, 148, 156, 158); ds 255 a fan (232, 234, 245) | `0x00435F20` (`0x0043609E..0x004360D5`) | CONFIRMED |
| effect flag `0x400` | besides one random slot a pass, on the element path the element heading is the orientation offset alone, without the parent's (158 Haste). On the `0x40` path it is the reverse: with `0x400` the offset plus the parent heading (`0x00438A03`), without it the offset alone (`0x00438BBB`); effect 284 is the shipped case | `0x00438DE0` | CONFIRMED |
| effect flag `0x1` | the floor hit effect is raised either way; `0x1` adds the Kill (runtime bit 4). At a wall, only with `0x1`, HitSolid is raised and the move fails; the update then zeroes the velocity and keeps the old point, no Kill (156 Disruption) | `0x00446C70`, `0x004476E0` | CONFIRMED |
| effect hit ids | on the floor: HitWater if terrain `+0x17B` has `0x100`, else HitLava for `0x80`, else HitSolid; HitSolid on a wall. Fireball trail 64 → 51 | `0x00446C70`, `0x004476E0` | CONFIRMED; the terrain bits' meaning STRONGLY INFERRED |
| element hit ids | on the floor: the first nonzero of HitWater, HitLava, HitSolid, no terrain test; HitSolid on a wall | same | CONFIRMED |
| element flag `0x40` | yaw := atan2 of the initial XY velocity, a mesh points along its flight (187 iceicle) | `0x00435F20` (`0x0043615F..0x00436190`) | CONFIRMED |
| element NextEffectId | that effect is made **every update** at the element (550 → 337) | `0x00436430` | CONFIRMED |
| effect light | made once at init, moved with the effect plus the descriptor offset, released at teardown, so it outlasts emission; light flags `0x2`/`0x4` are not tested there | `0x00439880` | CONFIRMED for that path |
| effect flags `0x80`, `0x200`; element flag `0x20`; effect Radius | no reader found among the decompiled functions' tests of the flags words (`0x004347E0`, `0x004376E0`, `0x004386F0`, `0x00439100`, `0x00440420`) and of `+0x6D` (only the loader `0x004358C0` and constructor `0x004357E0`); undefined functions were not scanned | | STRONGLY INFERRED inert |

**Generation types other than 4** (table `0x00697668`, five-byte rows: 18 entries; type 0 null):
- **18, `0x00439100`:**
  - does nothing without flag `0x40`;
  - otherwise makes `ElementsPerTurn >> 2` passes over slots 3..0, each making that id as an
    **effect** at the exact point with a random heading;
  - users: Lava Spill 69, Fireburst 87, Tremor 103, Horny Teleport 123/124, Graveyard Mist 130.
    All have HP 1, so each fires one burst.
- **14, `0x00439480`:** draws the decimal digits of instance `+0x12F` as elements (first id +
  digit). Gold Cost 22 and Mana Cost 364, through `0x00441060` (view `+0x64`).
- **6, `0x00439330`:**
  - ids form a range;
  - elements are born at the exact point with heading `Random(64) − 32`;
  - every third update it also makes the effect named by `+0x37`;
  - only Frost Ball 27 uses it.
- **3, `0x00438020`:**
  - a tile burst kicked by the effect's own speed fields;
  - the effect named by `+0x37` at 1 in 15;
  - only Black Knight Portal 182 uses it.

## 4. The shipped shot effects that matter

| shot | trail (`+0x6D`) | other |
|---|---|---|
| 1 Fireball | 64 (fire trail + sputter, light 44,150,20) | hit-thing 234 |
| 12 Arrow, 35 Guided Bolt | 333 / 338 sparkle trails | hit-thing 232 / 234 |
| 16 Firebomb → 34 | 213 | explosion's creation 334 |
| 15 Inferno | — | creation 107 → effect 106 (§5) |
| 21 Thunderbolt | — | body art type 7 kind 2; hit-thing 39 |
| 30 Lightning | — | body art type 7 kind 7; creation 283 (Linechrg flash, light 150,50,255) |
| 8 Freeze, 6 Haste, 9 Armour, 7 Invulnerable, 42 Drain, 43 Disruption | 77, 158, 212, 150, 134, 156 | hit-thing 114, —, 362, 149, —, 231 |

The full per-shot list is the data: `dk2tools kwd records Shots.kwd` names them, and every
reachable effect and element is in `Effects.kwd`/`EffectElements.kwd`.

## 5. The Inferno

**The carpet (CONFIRMED data, flag `0x40` CONFIRMED binary).**
- Shot 15's creation effect 107 (type 4, HP 1, flags `0x240`) makes **effect 106, "Inferno
  Data 1"**, once, at the cast point.
- Effect 106:
  - type 4, HP 112 (5.6 s at 20 updates a second), 8 per pass. GenerateRandomness 3 makes
    `0x004386F0` skip the wave unless `Random(4)` is 0, so about 28 waves over its life;
  - elements: explosion 208–211, "inferno carpet" 434 and "Inferno spread" 435, origin ring
    0..16, height 30;
  - a light (144,64,100) of radius 10240;
  - Next effect 1 (Explosion, which chains the Fire Ring 143), death effect 105 (rising mist 436).
- The shot itself never moves and has no trail. The keeper spell record carries no effect.

**The damage pass draws nothing.** Process 30 (`0x004AF660`) calls only `0x004B32A0`,
`0x0048EBD0(1, owner)`, `0x004B2CE0` and bridge command 10.

**The camera jitter.**
- Bridge command 10 → `0x004483A0` → `0x00441230` adds 1 to `+0x132B`, for the local caster
  only.
- `0x0043B220` at `0x0043C0D0` (and `0x00441250`) spends it one a view update. Each step adds
  `Random(200) − 100` to the camera offset (`0x00449B00`, camera `+0xD74..+0xD7C`) from the
  bridge's own generator `+0x72`.
- The mechanism is CONFIRMED; that it reads as a shake is STRONGLY INFERRED.

**Burning creatures (CONFIRMED, `0x0048E4B7..0x0048E631`, re-read).**
- `0x0048EBD0` stores ticks at `+0x469` and the burner at `+0x46B`. The Inferno sets 1 every
  tick (`0x004AF821`). A fire-category shot sets it only on a creature at or under a fifth of
  its health: 10 ticks by contact damage (`0x004AC526`), 5 by Modify Health (`0x004ADC4C`).
- Each tick of `0x0048E160`, while `+0x469` is nonzero:
  - it decrements it;
  - it keeps sound `0x340` from the creature's bank running;
  - **three times** it draws z, then x, then y from `0x005558D0` on the **simulation** stream
    (`[0x006E5050] + 0x15A0`, [dk2-random.md](dk2-random.md)) and requests **effect 88**
    through `+0x38C`. The point is the creature's z plus `Random(vtable +0x40)`, and x/y the
    position less half the footprint `def+0x63A` plus `Random(footprint)`.
  - The flames are free-standing, not tied to the creature.
- At zero it clears the overlay (`0x004B4610`) and `+0x46B`, and stops the sound.
- The chooser `0x0048BE50` gives a burning creature the additive red material (world
  `+0xA36B`, at `0x0048BEE8`). Frozen, poison and invisible take precedence.
- The draws make burning a **simulation** matter as well as a look: none is modelled in ours,
  so the stream diverges from the first burning tick (Q658).

## 6. Procedural lightning (art type 7)

**Kind.**
- The kind is the first dword of the art resource's type block at file `+0x44`
  (`KwdArtResource.Unknown_0044`), not its name (CONFIRMED data):
  - Thunderbolt 21: 2;
  - Lightning 30, Lightning Trap 59, Deconstructor 63: 7;
  - Gas Missile Cloud 55: 2;
  - effect 4 Lightning Bolt: 8;
  - effect 28 Frost Ball Ring: 4.
- `0x005A01A0` dispatches on it:
  - 1: a scrolling 5×5 quad grid (no shipped user found);
  - 2: one bolt;
  - 3: the selection cage ([hand-cursor-rendering.md](hand-cursor-rendering.md));
  - 4 and 6: two eight-segment "Ice Ring" rings;
  - 7: three bolts;
  - 8: one bolt to a fixed end.
- Textures: the render sink's init (`0x005984E2..`) loads "Lightning", "Cursor", "Ice Ring" and
  "White".

**Endpoints (`0x005A0450..0x005A05C4`, CONFIRMED).**
- The start is the thing's drawn position.
- For kinds 2 and 7 on a shot:
  - the end is the target `+0x147`'s base point, with no height offset;
  - a dead target, or a room (class 9), clears the link and the bolt is never drawn again;
  - a live creature caster `+0x16D` replaces the start's x and y with its interpolated position
    (`0x0044CE70`), keeping the shot's z.
- Otherwise, kind 8 included, the end is straight down or up at height `0x1000` (sign
  STRONGLY INFERRED, Q661).

**The bolt (`0x005A0030`, CONFIRMED).**
1. Subdivide recursively at midpoints (`0x0059F940`) until a segment is at most 0.06 tile.
   Each midpoint moves on every axis by `U(−1,1) × amp × len / 2048`, with amp the dword
   `0x006BE8F0` = **192** (±9.4% of the segment).
2. A fork spawns when a quarter of the bolt's length exceeds 0.2 and the raw `rand()`
   (0..32767) is below `256 / (len / 4)`, a chance of that over 32768 per subdivision. It runs
   to the end jittered by ±len/4, and forks recurse.
3. `0x0059FBE0` halves the node list until at most 32 nodes remain. It then builds a
   camera-facing ribbon of half-width 0.03 tile, mitred by averaging the neighbouring normals,
   zero-width at both ends.
4. `0x0059FDD0` draws one double-sided quad per segment: U 0..1 along it, white, texture
   "Lightning", flag `0x1000`.

The randomness is the C runtime `rand` (`0x006359F0`), never the simulation's. Nothing is
cached: **a new shape every rendered frame**.

**Thunderbolt (shot 21).**
- It draws one bolt from the shot, made `0x2000` above its target
  ([keeper-spell-thunderbolt.md](keeper-spell-thunderbolt.md) §2), to the target's feet.
- It draws for its 8 ticks of health (bit 2).
- The hit-thing effect 39 shows 16 sparks a pass plus smoke at the target, carried with it.
- No light, flash or shake was found on the path.

**Creature Lightning (shot 30).**
- Three bolts per frame from the caster's moving x/y at the spawn height to the target's feet,
  for 8 ticks.
- Its creation effect 283 is a flash of `Linechrg` sprites with a violet light, made at launch.
- The trap's shot 59 starts at the shot.

**Electrified creatures.**
- `+0x4E3`, "LightningTimer", is set to 20 by process 9.
- `0x0048BE50` decrements it and clears thing `+0x92` bit 1 each tick (`0x0048BEBE`). It sets
  the bit again only when it installs a new animation while the timer runs; the stun's action
  52 normally does.
- With the bit set, `0x005A3AA0` (after the creature's mesh draw) passes the mesh control
  points with id below 2000 to `0x005A1340`. That draws bolts at amplitude 256 between pairs of
  points not in the same run of consecutive ids, each pair at chance `1/(8i)` a frame, from the
  render seed `0x00795850`.
- Crawling bolts on the body, no tint. How long they show in play is Q660.

## 7. Ours

- **Built:** the effect engine's type 4 with `0x40`, `0x400` slot choice, Next chains, the
  attachment arm for creatures, lights and their flags, type-5 mesh elements.
- **Not built:**
  - every shot effect request of §2 (no trail, creation, timed, hit or death effect);
  - the shot attachment arm;
  - flag `0x100`'s carried velocity;
  - DirectionalSpread;
  - `0x400`'s heading;
  - effect flag `0x1`;
  - the hit ids of effects and elements;
  - element flag `0x40` and element NextEffectId;
  - generation types 18, 14, 6 and 3;
  - creature burning (simulation and display);
  - the Inferno camera jitter;
  - art type 7.

## Confidence

**CONFIRMED (binary, 2026-10-26):** §1; §2 except the dead lava/water fields; §3's table rows
so marked; the generation types; §5's burning arm, re-read in the listing; §6's kinds,
endpoints and bolt arithmetic.

**CONFIRMED (data):** the shot and effect ids, effect 106's contents, the type-7 kinds.

**STRONGLY INFERRED:** hit-lava/water inert; bodies without pitch; inert effect flags `0x80`,
`0x200`, element flag `0x20` and effect Radius; the terrain bits' water/lava meaning; the
jitter as a shake; the kind-8 end height's sign.

**UNKNOWN:** Q658–Q662.
