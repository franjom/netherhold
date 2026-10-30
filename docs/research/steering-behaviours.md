# Mechanic: the steering behaviours' bodies (phase 2 of ADR-0157)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `structure-blocks.md` §5 for the object enumerators this page finds installed

Original version: DK2 v1.70 (GOG)

Questions: **Q349** (the bodies of `avoid_walls` and `follow_path`, and the
integrator's obstacle clipping) and **Q344** (`grtc:`). Continues
`navigation-and-steering.md`, which owns the combination, the constants and
the integration; this page owns what each behaviour computes.

**Built and wired** (ADR-0157, `src/DK2.Simulation/Steering/`): the three
behaviours below, the route window, the turn and the 16.16 helpers, beside
`SeparationRules`, and `SteeringIntegrator`: the combiner, the Euler step, the
slide and the wall clamp. Phase 3 runs them for every creature:
`CreatureMovementSystem` over `WorldSteering`. Section 5 is the mover proper:
what `Follow_Path` aims at, that a creature **turns before it moves**, and how
the route window is refilled.

---

## 1. The four installed callbacks

The steering initialiser `FUN_004D10D0` registers the behaviours (`0x004D56E0`,
`navigation-and-steering.md` §3.3) and then installs four function pointers
through the setters at `0x004D2770`–`0x004D27D0`, each storing a function and a
context pointer (`[0x006ED40C]`-`[0x006ED418]`) that is passed back as the last
argument:

| slot | function | what it is | used by |
|---|---|---|---|
| `[0x006ED3FC]` | `0x004D1170` | [tile-rectangle creature query](creature-neighbour-query.md) | `Separation`'s neighbour query |
| `[0x006ED400]` | `0x004D12B0` | the enumerator that **gates on `Obstacle`** and forwards position, size, flags | `Repel_Obstacle` |
| `[0x006ED404]` | `0x004D15E0` | the **crowd probe**: the signed sum of creature word `+0x200` over the creatures on a square of tiles (section 5.4) | `Follow_Path`'s seek |
| `[0x006ED408]` | `0x004D16B0` | the **navigation-byte probe** at a position (reads `world+0x9F2`) | `Avoid_Walls`' feelers |

So `structure-blocks.md`'s inference — that furniture is continuous collision
geometry rather than a blocked tile — is now closed from the consumer's side:
the `Obstacle` enumerator's only steering consumer is `Repel_Obstacle`.

## 2. `Repel_Obstacle` (`0x004D31D0`, weight 0.4)

Two exemptions first, then the work.

- **Arrival.** If `path_flags` bit 2 is set and the body is on its **last**
  waypoint, and the octile distance to it is at most `radius + 1.0`, the force
  is zero. The same shape as `Separation`'s exemption (§3.2 of the steering
  page): nothing is shoved about once it has arrived.
- **Stuck.** If `+0x10F` bit 1 (`escape_stuck_mode`) is set, the force is zero.

Otherwise it points the body's two leading dwords at a **twelve-slot scratch
list** at `body+0x8`..`+0x80` (ten-byte records; count at `+0x80`), zeroes the
accumulator, and asks the `Obstacle` enumerator for everything within
`radius + 1.0`, with `0x004D3020` as the per-obstacle callback:

1. `v = body.pos − obstacle.pos`, `d = |v|` through the three-band integer
   square root, `n = normalise(v)` (`0x004D1F10`). `d = 0` contributes nothing.
2. `reach = obstacle.size + body.radius`. Nothing happens unless `d ≤ reach`.
3. If the obstacle's flag **bit 0 is clear**: `force += n × max_force`. Full
   force, straight away, **no falloff** — contact is binary.
4. If bit 0 is **set**: the obstacle is not pushed against but **recorded** —
   `n`, and a word `1 | ((flags & 2) << 1)` — into the scratch list, up to
   twelve. The integrator (`FUN_004D4760`) walks that list after the forces are
   summed and clips velocity against it (Q349).

Bit 0 selects slide versus shove, but is not a direct copy of the definition's
`SolidObstacle`: the query retains and toggles it as documented in
[structure-blocks.md §5](structure-blocks.md#5-objects-collide-continuously-the-navigation-cache-is-separate-q209).
Object order therefore matters even when a callback contributes no force. The enumerator
skips the `Dungeon Heart Obstacle` (object 100) in its tile walk and forwards
the one on the heart room's centre once per query that touches heart terrain,
with flag bits 0 and 1 (`structure-blocks.md` §5.3): a slide ring of 1.5 tiles
plus the body's radius, which Level 7's imps rode in the capture.

Two helpers seen on the way: `0x004D4600` projects a target onto the body's
`local_xaxis`/`local_yaxis` (forward and lateral offsets, 16.16), and
`0x004D2070` is probably a **ray–circle intersection** along the point's own +x
axis (no heading argument), `0x80000000` for a miss. Both serve `0x004D32E0`, a
repel-along-heading routine whose caller is not yet identified.

## 3. `Avoid_Walls` (`0x004D35C0`, weight 0.6), the normal path

The steering body's positions are **16.16 tiles**: `0x004D3BE0` turns one into a
tile by `>> 16`, `0x004D3C10` turns a tile corner back by `<< 16`, and the
router's `<< 8` copy of its 256-per-tile waypoints lands exactly there.

1. `ahead = pos + vel` (`0x004D5080`, the predicted position one step on).
2. `tile = pos >> 16`; `own = probe(tile)`, the body's own navigation byte.
3. `dir = vel / |vel|`; `sx = sign(dir.x)`, `sy = sign(dir.y)` — each is `−1` when
   negative and `+1` otherwise, never zero. `len = |vel|` is the look-ahead.
4. Three probes through `0x004D16B0`, each run through the transition-cost
   function against `own`, so "wall" means *impassable for this mover*:
   `A = (x, y+sy)`, `B = (x+sx, y+sy)`, `C = (x+sx, y)`.
5. **Only the diagonal blocked** (`B` wall, `A` and `C` open): the corner of
   tile `B` nearest the body is taken, and a ray from `pos` along `dir` is
   tested against a circle of `radius` on that corner (`0x004D20F0`). If it
   hits within `len`: `hit = pos + dir·t`, `n = (hit − corner) / radius`, and
   the force is **`2·((hit − ahead)·n)·n − vel`** — a reflection off the corner,
   as a steering force (desired minus current).
6. **Otherwise, per axis.** If `C` is a wall: with `sx > 0` the face is
   `(x+1) << 16` and the force is `2·(face − radius − ahead.x)` when `ahead.x`
   is past that limit; with `sx ≤ 0` the face is `x << 16` and the force is
   `2·(face + radius − ahead.x)` when short of it. The same for `A` on y with
   `sy`. Both axes are judged independently and both may contribute.

The ray-circle test (`0x004D20F0`) is not the textbook's. With `b = 2(d.m)` it
forms `4(b^2 - ac)`, takes the integer square root of that 16.16 value unscaled,
and returns `(-b - root) / 2`, or `INT_MIN` when the discriminant is not
positive. The "hit" therefore lands almost on the corner's centre, and the
reflection fires only when the corner is within one step; a body approaching
from further off gets nothing. Reproduced as read (`RayCircle`).

So `Avoid_Walls` is a **wall margin of one radius (0.2 tiles)** enforced against
the predicted position, with a reflected push at inside corners. It is what
keeps a body off the wall the funnel's corners hug. The block under
`escape_stuck_mode` is section 6.

## 4. The integrator: slide, step and wall clamp

`FUN_004D4760` runs, in this order: the weighted sum of section 3.3 of
`navigation-and-steering.md`; the slide; the sum clamped to `max_force` and
divided by a mass of 1.0; `vel += accel`; `vel` clamped to `max_speed`;
`next = pos + vel`; the arrival snap; the wall clamp; the local axes; the stuck
history (section 5.1). Built as `SteeringIntegrator`.

**The slide.** The twelve-slot list `Repel_Obstacle` filled is walked from the
last record to the first. For each unit direction `n` from the obstacle to the
body: `inward = (F + vel) . n`, on the **tentative** motion; a record with
`inward >= 0xCCC` (0.05) is skipped. Otherwise both `F` and `vel` lose their
component along `n` through `I - n n^T`, **x first and y from the already
updated x**, so the result is not quite the ideal projection. The tangent is
`t = (-ny, nx)`; if `(F' + vel') . t < 0` it is negated. `t x max_force` is
gathered, and after the walk the gathered pushes are added to `F`. (A filter
comparing `(n >> 18)^2` sums against the largest seen is inert for unit
normals.)

**The snap.** With `path_flags` bit 2, on the last window point, when the
octile distance still to go is **less than** the octile length of `vel`, `next`
is the point and `vel` is zeroed (the tick, the flags, a capture check: `imp-walk-arrival.md`).

**The wall clamp.** `own` is the byte of the tile the body stands on; a tile is
open when the transition cost from `own` to it is nonzero. If `next`'s tile is
open the body moves there. Else the x move alone, if its tile is open, with
`vel.y` zeroed; else the y move alone with `vel.x` zeroed. Else, with
`(dx, dy)` the tile delta and `step = next - pos`: if the tile a quarter turn
left, `(ox - dy, oy + dx)`, is open the body moves by `(-step.y, step.x)`; else
if `(ox + dy, oy - dx)` is open, by `(step.y, -step.x)`; **the turned step is
taken without a look at the tile it lands on**. Otherwise the body stays, `vel`
is kept, and an extra `1` is pushed into `path_stuck_buffer`.

**The axes.** `local_xaxis = normalise(vel)` after the movement clamp;
`local_yaxis` is its perpendicular `(-y, x)`.

## 5. `Follow_Path` (`0x004D3C40`, weight 0.35)

Arguments: the force to fill, the body, and the creature entity. It reads the
definition's `+0x686` bit 0, `IsWorker`, and has **an imp arm and a creature
arm**. Both start with the same bookkeeping.

### 5.1 Progress and the stuck history

`progress = 1 - remaining / path_total_dist` in 16.16, where `remaining` is the
octile distance from `pos` to the current subpath point plus
`path_subpath_dist` (`+0xFE`, the window's unwalked segments) plus
`path_tail_dist` (`+0x102`, the route beyond the window). All three are in
**sixteenths of a tile** (a 16.16 octile `>> 12`). It is 1.0 when
`path_finished` is set or the total is zero. `0x004D54F0` computes the same
thing for callers.

The **integrator** owns the history: at the end of `FUN_004D4760` it shifts
`path_stuck_buffer` (`+0x10A`) left and sets bit 0 when this tick's progress is
**not greater than** `path_last_progress` (`+0x106`), then stores the progress.
It pushes a second `1` in the same tick when its own wall clamp refused the
step (`0x004D4E2B`; the clamp itself is codex's read of the integrator).

`Follow_Path`, when progress is below 1.0, takes `stuck = popcount(low 15 bits)
/ 15`. If `stuck >= 0x5555` (five of fifteen ticks): re-request the route from
where the body stands (section 7), put the history word back (the request
clears it), and set `escape_stuck_mode`. Then, if `escape_stuck_mode` is set
and `stuck <= 0x2222` (two of fifteen), clear it.

### 5.2 Which point it aims at

No route (`path_total_count` 0) or `path_finished`: the force is **`-vel`**, a
brake. Otherwise `idx` is bits 3-5 of `+0x10E` and `last` means
`idx == path_total_count - 1`.

Creature arm, `last`: `d = path_target_pos - pos`; the arrival threshold is
`radius x (path_flags bit 0 ? 0.1 : 0.5)`, so **0.02 or 0.1 tile**; if
`octile(d)` is under it **and** `octile(vel) <= 0x6147` (0.38), `path_finished`
is set. The aim is `path_target_pos`.

Creature arm, not `last`: `p = subpath[idx]`. `near` is `octile(p - pos) <
1.2 x radius` (0.24 tile). `occupied` (`0x004D2850`, callback `0x004D27F0`) is
"some `Obstacle` within 1.2 tiles of `p` has `octile(obstacle.pos - p) <=
obstacle.size`". If `near`, or `occupied` with `octile(p - pos) < 0xCCCC`
(0.8): **advance**: `path_subpath_dist -= segment[idx]` (five dwords at
`+0xE6`), `idx++`, and if `idx` now equals `path_buffer_count` (bits 0-2 of
`+0x10E`) re-request the route from the current position (section 7). The aim
is `subpath[idx]`.

Imp arm: on `last`, `path_finished` when `octile(path_target_pos - pos) <
0x1999` (0.1 tile), with **no speed test and no radius scaling**; otherwise
advance when `octile(subpath[idx] - pos) < 0x8000` (**0.5 tile**), with no
obstacle test. Imps therefore cut corners half a tile early; creatures a
quarter.

### 5.3 Turning comes first

Both arms then call `0x004B2AE0(entity, aim >> 4)`, the entity-level turn.
`aim >> 4` is in 1/4096 tile, the unit of the entity position at `+0x1A`/`+0x1E`
(`combat.md` section 4). It takes the bearing to the aim with the same `atan2`
(`0x0065FB54`, **2048 to a turn**), the shortest angular distance to the
heading at `entity+0xF4` (`0x005119F0`), and if that is nonzero steps the
heading toward the aim by `min(distance, rate)` (`0x00511A30` picks the side)
and recomputes the distance. The rate is a hook at `entity+0x12` when set, else
the definition's `+0x6A0 & 0x7FF`.

**If the heading is not yet on the aim, `Follow_Path` returns `-vel`.** A
creature stops to turn, and moves only once it faces its waypoint.

The rate's loader (`FUN_00492320`) reads the KWD creature record's `u16` at file
offset **`0xF20`** and divides by variable 19, the four game ticks a second,
flooring at 1 when the file value is nonzero. The shipped values, in 2048ths of
a turn: Imp `4096` (1024 a tick: half a turn), most creatures `2048` (512),
Warlock, Vampire and Skeleton `1365` (341), Troll `1024` (256), Bile Demon `682`
(170, so a U-turn costs it seven ticks). Built as `SimulationTiming.TurnRatePerTick`,
which every creature turn uses (ADR-0602).

### 5.4 The seek

Facing the aim, the force is a desired velocity minus `vel`:

- **Creature arm, `last`, `path_flags` bit 1 clear** (the final approach):
  `d = aim - pos`, `len = |d|` (three-band `isqrt`); `len == 0` gives a zero
  force; `speed = min(max_speed, len / 2.0)`; force `= d x (speed / len) - vel`.
  So it **decelerates inside 0.8 tile** of the destination.
- **Otherwise** (creature intermediate points, or bit 1 set; and the imp arm
  always): `c = min(1.0, crowd)` and `k = 1.02 - c` for creatures (`0x1051E`) or
  `1.825 - c` for imps (`0x1D332`); force `= (aim - pos) x k - vel`. `crowd` is
  the probe `0x004D15E0`: the signed sum of creature word `+0x200` over every
  creature on a tile of the **0.95-tile square** centred on `subpath[idx]`
  (edges by floor and ceiling; a square, not a circle). The creature
  initialiser writes that word as `(v >> 1) << 4` (`0x00487B0B`), where `v` is
  virtual slot `0x3C` of `CCreature` (vtable `0x0066D524`, `0x004B5D60`): the
  definition word `+0x63A`, loaded from the KWD creature `u16` at `0xEA0`, a
  radius in 1/4096 tile (Imp 573, Bile Demon 1740; our name: footprint radius).
  So each creature near the point takes half its footprint radius, in tiles,
  off the gain: an imp 0.07, a Bile Demon 0.21. Four Bile Demons round a
  waypoint bring a creature's gain from 1.02 to 0.18; imps hardly slow.

## 6. `Avoid_Walls` in `escape_stuck_mode`

The block at `0x004D39D5` runs **after** the normal path of section 3 and adds
to its force. `v = (subpath[idx] - pos) + vel`; its dominant axis gives a unit
step `s` of `(+-1, 0)` or `(0, +-1)`; `t = pos >> 16`; `P = (s.y, -s.x)` and
`Q = -P` are the two perpendiculars. Four tiles are probed through the nav
probe and the cost function against the body's own tile: `t+P`, `t+s+P`,
`t+Q`, `t+s+Q`. If both `P` tiles are passable the nudge is toward `P`; else if
both `Q` tiles are, toward `Q`; else `P`.

The nudge is **`+-2` raw units**: the integer signs are doubled and added to the
16.16 accumulator with no shift, so it is 2/65536 of a tile. In the shipped
game this block is a tie-break for a zero force, not a push. Reproduce as is.

## 7. The mover's route request (`0x004D5100`)

Called by `Follow_Path` when stuck or when the window is exhausted, and by the
state machine when a destination is set. It calls the dispatcher `0x004D0E10`
with `pos >> 8`, `target >> 8` and the fraction `0x2000`; the result count is
`[0x006ECB28]` and the points, 256 to a tile, start at `[0x006ECB2C]`.

- **No route:** `path_target_pos` stored, counts and the four distances zeroed,
  history zeroed, `path_finished` set, index and buffer count cleared,
  `path_flags` set from the argument; returns false.
- **A route of `n` points:** `path_total_count = n`; `path_buffer_count =
  min(n, 5)`; `path_total_dist = octile(p0 - pos) + sum of octile(p_i -
  p_i-1)`, each `>> 12`; the first `min(n, 5)` points copied `<< 8` into
  `subpath_buffer`; `segment[k] = octile(sub[k+1] - sub[k]) >> 12` and
  `path_subpath_dist` their sum; `path_tail_dist` the same sum over the points
  beyond the window; `path_last_progress` and the history zeroed; index and
  `path_finished` cleared; returns true.

So the window **does not slide**. When five points are consumed the whole route
is requested again from wherever the body is, and the funnel runs afresh.

### 7.5 The creature's tick (`0x004D5A40` and its callers)

The body is creature `+0x124`. Callers `0x00470C80`, `0x00471460`, `0x004769D0`
and `0x00488930` read its velocity down four (`0x004D5AF0`), then write entity
position/velocity up four (`0x004D59A0`), preserving 1/4096 precision; they set six navigation globals,
integrate (`0x004D5A40`, unless speed is zero),
read position down four (`0x004D5AC0`) and commit (`0x004B5560`). The main update
first rejects off-map or solid positions through `0x004B35C0`, resetting body position and velocity.
It requires state-table bit 0 at `0x006DA8C8 + state × 25`; route-less fight dispatch and remembered-entity activation are in [combat-spacing.md](combat-spacing.md).

**Speed.** Walk/run/shuffle: `def+0x5B4/+0x5B8/+0x5BC`, per tick. Loader `0x00492320`
and stat setter `0x00494BA4` divide KWD rates by variable 19,
floored at one. `0x004887D0` selects a mode or explicit speed; `0x0048D010`
stores `min(speed, 0xFFF)` at `+0x11C` and shifts it up four into `max_speed`.
Timed multipliers: creature-status-effects.md §2. Radius: `(footprint >> 1) << 4` through vslot `0x3C`
(`0x00487AF3`); `max_force` keeps the reset's `0x18000`.

**Water (2026-10-10, Q118; CONFIRMED assembly).** `0x00488930` at `0x0048970B`–`0x00489749`
tests terrain Water (runtime `+0x17B`, `0x80`).
Unless flying (`0x0048ED40`, vslot `+0x88`), it sets current speed ×50/100,
unsigned floor, through `0x0048D010`: in body units, `((cap >> 4) / 2) << 4`.
After loader rounding/clamping, the ratio holds across levels; non-Water bridge terrain is skipped.
Neither `ShuffleSpeed` nor `CanWalkOnWater` enters this modifier.
The independently recomputed grounded-water flag `+0x3EA` bit 21 is not its gate.
Order: non-worker low-health half-speed, water, player bonus, possession/stat modifier.
At tick end persistent speed is restored unless `+0x3BF` was set; water does not set it.
Flying's worker bits, five animation exceptions and mode-dependent bit precede
`CanFly` outside states 217–218. Ours supports the trap subset: workers grounded,
otherwise `CanFly` outside 217–218; other flight arms remain deviations.
Lava's adjacent non-flying 150 % arm remains unbuilt. The owner saw slowdown; no live ratio was timed.
Ours: walk/run (`MaxSpeedOf`), timed modifiers and `WaterSpeedRules`; shuffle/state gate remain unbuilt.

### 7.6 The state's option word (`0x004D0F60`)

A state does not call the request itself. Two helpers (travel-states.md §2) —
`0x00471890(target*, next state, options)` for a position and `0x00471A70(room,
next state, options)` for a room — check the target tile (`0x004B35C0`) and call
`0x004D0F60(creature, target*, options)`, which unpacks the **option word**:
bits 1, 2 and 7 go to `creature+0x3E6` bits 17–19, the cost function's door
globals (navigation-mesh-and-search.md §4); bit 3 sets `path_flags` bit 0, bit
4 sets bit 1, bit 5 or 6 sets bits 2 and 3 together. Bit 0 stays with the
helper: on a failed request a hero worker tunnels and its party follows (§2.3
there). The stop `0x004D1070` requests the creature's own position with word 0. So **`path_flags`
bit 3 never travels alone and bit 1 is never set**: the values that occur are
0, `0xC` and `0xD`. Of 134 call sites (`reverse-engineering/pf-precall3.txt`,
`pf-precall4.txt`), 118 push a literal; states by handler id
(creature-state-table.md):

| word | flags | states |
|---|---|---|
| `0x80` | 0 | 45 sites: room jobs 53, 63, 108; eating's walk to the hatchery 91, 96; guarding 97; prisoners 115, 117; praying 101; wandering 86; fleeing 220; drinking 145; recuperating 266; advancing 223 |
| `0x00`, `0x01` | 0 | imps en route 8, 9; attacking 177, 181, 183; battle 200; stealing 160, 161, 166, 167; training 109; patrol 100 |
| `0xE8` | `0xD` | going to bed 81; the chicken 92; wages 187; gambling 155; pit-fighting 139 |
| `0xE0` | `0xC` | making a home 80 |
| `0x68`, `0x69`, `0x6C`, `0x6E` | `0xD` | freeing comrades 121; a captive's chicken 124; torturing 127; stealing 162, 163; leaving 195 |
| `0x20`, `0x41`, `0x26` | `0xC` | torturing 127; idle 232; helpers of 223 and 273 |
| `0x02`, `0x06` | 0 | `PatrolState.cpp`; `WorldTrigger.cpp` |

Ours: `MoveOptions` names the words and `WorldSteering.TryRoute` takes one per
request; the door bits are inert until doors enter the navigation byte.

## 8. What this leaves

- The sixteen request sites that push a register (§7.6), among them the main
  update's own at `0x00489E17` and states 1, 86, 100 and 186.
- First-person obstacle classes and the virtual Heart approximation (structure-blocks.md §6);
  the remembered entity of `Separation`'s second term; shuffle speed and the
  remaining update speed modifiers (§7.5); the per-state movement gate.
- Two floor tiles that touch only at a corner: no route, ours or DK2's
  (`0x004C9400` refuses before any search; its fill `0x004C91A0` is edge-only),
  `GridPathfinder` included since ADR-0339; but the mover commits a step by the
  tile it lands in, so a steered crossing is not forbidden. The Level 7
  capture cannot say: its cache holds no corner-only floor pair, and none of
  its 289 diagonal tile changes in 41,654 steps passed between two solid tiles.
  Level 10's capture (216,882 steps) has none either, but its three such
  corners saw three steps in all, so the question stands. Next: a target
  reachable both through a diagonal gap and by a 20-tile detour — routing takes
  the detour, steering cuts the corner; an Imp and a Bile Demon differ if it is
  steering (the wall margin is one radius).

## Confidence

**CONFIRMED (binary):** the four installed functions and which behaviour calls
each; `Repel_Obstacle`'s two exemptions, its query radius, the overlap test, the
full-force push for flag-bit-0-clear obstacles and the recording of the others;
the twelve-slot list and its record shape.

**CONFIRMED (binary + repo):** ordinary-query flag conversion and the separate
first-person/Heart streams are recorded in structure-blocks.md §5.

**CONFIRMED (binary):** `Avoid_Walls`' normal path as stepped in §3 — the
prediction, the three probes and their tiles, the one-radius margin per axis
with its factor of two, the corner case and its force; that steering positions
are 16.16 tiles; the slide's threshold, projection and tangential push.

**CONFIRMED (binary, 2026-09-17):** `Follow_Path` as stepped in section 5: the
progress formula and its units, the history's writer and both thresholds, the
two arms and their advance and arrival rules, the turn-before-move gate and
the turn rate's source and loader, both seek laws; the stuck block of section
6 including its raw-unit nudge; the route request of section 7 end to end.

**CONFIRMED (binary, second reader codex, 2026-09-17):** the slide's inward
test, projection order and tangent sign; the wall clamp's five arms and its
stuck bit; the ray-circle test to its return; the crowd probe's square and sum.

**Corrected 2026-10-15 (binary, read whole):** `0x004D2070` is *not* `0x004D20F0`'s
shape, as once inferred: +x ray, unscaled squares, one root (`functions/004D2070.md`).

**UNKNOWN:** nothing on this page; the KWD field names at `0xEA0`, `0xEA8` and
`0xF20` are ours until the reader gains them.
