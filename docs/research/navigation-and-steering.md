# Mechanic: how DK2 routes and how it moves (Q5)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The binary's own RTTI class names and debug format strings, which are
  DKII.EXE's identifiers rather than anyone else's (§1)

Original version: DK2 v1.70 (GOG)

Questions: **Q5** (answered at the architectural level here), **Q342**,
**Q343** and **Q344** (answered, built). The mesh and the search are Q341 and
the triangulation Q347, both answered in
[navigation-mesh-and-search.md](navigation-mesh-and-search.md).

**Both halves are built and wired** (ADR-0157): the router in
`DK2.Pathfinding` (`navigation-mesh-and-search.md`), the four behaviours, the
route window, the turn and the integrator in `DK2.Simulation.Steering`
([steering-behaviours.md](steering-behaviours.md)), and `WorldSteering` with
`CreatureMovementSystem` between them and the world (§6).

---

## 1. First, a provenance correction

Six documents here said Flame "names a 36-byte `Follow_Path` struct". Wrong:
`Follow_Path` is **DKII.EXE's own class name** — MSVC type descriptor at
`0x006A15D8`, complete object locator at `0x0067DBC8`, vtable at `0x0066DB74`,
and the constructor copies the lower-case literal `follow_path` into the object
(§3). Flame knew the name because the binary contains it. ADR-0080's rule is
unaffected in substance, but this name never needed it.

## 2. DK2 does not route on a grid

The navigation module occupies roughly `0x004C9000`-`0x004D6000` and carries its
own diagnostic strings. Read together they describe a **triangulated navigation
mesh**, not a tile search:

| string | what it establishes |
|---|---|
| `Heap Overflow (Triangles %d, Map %d*%d)` | triangles are allocated against map area |
| `default.pt`, `default.tr` | point and triangle files (§5) |
| `edge_points : edge not found %d->%d` | triangles are joined by shared edges |
| `Fwd route`, `Bak route`, `Bak reverse choice` | the route search runs in two directions |
| `EndGates`, `gate`, `way=%d`, `route=%d` | a route is a sequence of gates, not tiles |
| `Route2path`, `rtp:Exceeded max path length` | a second stage turns a route into a path |
| `grtc:Exceeded max path length` | the gate re-walk's 256-record diagnostic (§2.1) |
| `NavigationSystem:`, `NavigationSystemMap:` | the module's own two-layer naming |

The shape is the standard one for mesh navigation: search the mesh for a
**route** (a sequence of adjacent triangles and the gates between them), then
run `Route2path` to reduce that corridor to a **path** of waypoints. The mesh's
layout and the search — greedy best-first, run from both ends, shorter wins —
are read in [navigation-mesh-and-search.md](navigation-mesh-and-search.md).

### 2.1 `Route2path` is a funnel, and the waypoints are gate midpoints (Q342)

Read by a second reader (codex, 2026-09-16) and spot-verified here at the three
points that carry the conclusion. `Route2path` is **`0x004CEF40`**. It takes the
start, the end and an ordered triangle route; for each adjacent pair,
`0x004CCD50` returns the two endpoints of the shared edge — the **gate**.

The reduction is the standard **funnel** (string-pulling): keep a left and a
right bound from the current apex using signed cross products; when one bound
crosses the other, emit that bound's gate endpoint as a waypoint, move the apex
there and continue. The destination is the final, degenerate gate. Output is the
start, the end, a waypoint count, the 2D waypoints and the accumulated Euclidean
length. **Nothing in it rounds to a tile**: no `0x1000` mask, no shift by 12.

The steering body does not take that output raw. `0x004D5100` — called from
`Follow_Path`'s own virtual, `0x004D3C40` — asks the dispatcher `0x004D0E10`
for the gate branch with the literal **`0x2000`**. That branch runs the driver
(mesh page §3.1); `0x004D0150` then funnels the kept route again in walking
order and hands it to the **gate re-walk `0x004CF490`** (diagnostic prefix
`grtc:`; Q344, read by two readers and built as `GateWalk`, 2026-09-17):

- **Kept gates.** A gate is dropped only when all four sight-lines from the
  last kept gate's endpoints (the start, before any is kept) to the next
  gate's endpoints pass between its own — eight signed 64-bit cross products,
  zero counting as between. The gates a straight route sees through all go.
- **Selected endpoint.** The one nearer the funnel polyline's current point
  (ties take the second); a nearer next point advances the cursor first.
- **The cap** is a literal **`0x1000`** pushed by `0x004D0320` (`0x004D0967`) and passed
  on by `0x004D0150`: sixteen tiles in router units; the mover's `0x2000` never reaches it. A kept gate within a
  tile of the polyline is cut to fifteen tiles from its selected endpoint if
  it is sixteen or longer (`0x004CFC50`). Otherwise the *line* through the two
  polyline points around the cursor is intersected with it (`0x004CFFD0`: Q14
  parameter within ±1, then the gate's box; the crossing is written before
  that test) and `0x004CFDA0` leaves a gate whose ends are both within eight
  tiles of the crossing, collapses toward an end that is, or moves both ends
  to eight tiles from it. No gate under sixteen tiles ever moves; how often a
  shipped map has a longer one is unmeasured.
- **The point.** `0x004D0240` takes `fraction / 0x4000` of each kept gate
  (`IMUL` then `SAR 0xE`) from the selected endpoint — `0x2000` is **the
  midpoint** — then the destination. Up to **five** points (`CMP CL,0x5`) go
  into `subpath_buffer` at `+0xB4`, scaled by `<< 8` and nothing else.

The edge function `0x004CCD50` names the walker's **right-hand** endpoint
first: the original's triangles are wound clockwise in the cross product's
terms (the reset `0x004C8D10` stores `(3,1,0)` and `(1,3,2)` over corners
`0..3`), ours the other way, so `GateWalk` takes `Funnel.Gate`'s right as A.

When no gate is kept the path is the destination alone — one segment at
whatever angle the destination lies. **That is the owner's straight 10-by-3
walk (§3.1), predicted from the binary before it was watched.** Around the end
of a wall the mover aims at the middle of the gap's gates, not at its corners.

`way=%d` prints the waypoint count and `route=%d` the triangle count; `EndGates`
is a timing label, not a data dump.

## 3. Movement is a steering system with named behaviours

The binary defines a `Behaviour` base class and **exactly eight** subclasses.
They are contiguous in the RTTI name block and nothing else in the image matches
the family:

`Seek`, `Arrival`, `Separation`, `Avoid_Obstacle`, `Repel_Obstacle`,
`Formation`, `Avoid_Walls`, `Follow_Path`. **Three of them are stubs** (§3.2).

That set is the classical steering-behaviour vocabulary. Combined with
`max_force`, `max_speed` and `projection_vector` on the mover (§4), the mover is
a force-accumulating steering system.

**Each behaviour is a startup-constructed global singleton.** `follow_path`
lives at `0x006ED388` and `avoid_walls` at `0x006ECAF0`; each constructor writes
the vtable at `+0` and copies its own lower-case name to `+4`, then registers a
destructor through `atexit` at `0x00634840`. `Follow_Path`'s constructor is
`0x004D2710` and it has exactly one caller, its own initialiser at `0x004D26F5`.

The consequence matters more than the detail: **a behaviour object holds no
per-creature state**. It is a named strategy, shared by every creature. A
36-byte `Follow_Path` is a vtable plus a fixed name buffer and nothing else.

`Follow_Path` declares one virtual method, `0x004D3C40`. It takes the mover and
the creature, reads the creature's compact definition through `creature+0x370`
(`combat-cadence.md` §2), tests definition `+0x686` bit 0, and early-outs when
the mover's `path_finished` bit or `path_total_dist` says there is nothing left
to follow.

### 3.1 Two owner observations, and only one of them decides anything

**Creatures push each other and do not pass through each other** — owner,
2026-09-16. This is `Separation` running, and it is the first *behavioural*
confirmation that the steering layer is not an implementation detail. Ours has
no mutual avoidance at all: `CreatureMovementSystem` says so in its own remarks,
and two creatures here occupy the same position without either noticing. Q345.

**The diagonal glide does not discriminate, and was not claimed to.** The owner
also reports imps crossing *the diagonal of a tile* smoothly rather than
stepping. That re-confirms Q48(a), but it separates nothing: a tile diagonal is
45 degrees, which is exactly what an eight-connected mover walking a tile-to-tile
route produces. Both models predict it.

**The walk that does separate them has been made, and the route is straight.**
Owner, 2026-09-16: ten tiles across and three up, over open floor, and the route
runs as a single straight line with no bend. That is about 17 degrees. An
eight-connected tile route cannot produce it — it is built from 45 and 90 degree
legs, so it must show a bend or a staircase.

**DK2's routes are therefore not tile-constrained. CONFIRMED.** Note what this
does and does not establish: it rules out a tile route, and it agrees with the
mesh reading of §2, but on its own it would equally fit any router that is free
of the grid. The triangles, gates and `Route2path` are what make it a mesh; this
observation is what makes our own router wrong where it can be seen.

### 3.2 Three of the eight do nothing, and Separation is read to its store

`seek`, `arrival` and `formation` share one method, `0x004D2A40`, a **stub**
that writes a zero vector. Five behaviours are live.

`Separation` is `0x004D2DF0`; its per-neighbour callback is `0x004D2B80`. The
steering body sits at `entity+0x124`, which is how the two connect.

**The outer routine.**

1. With `path_flags` bit 3 set, on the last window point or with
   `path_finished`, and within `radius + 0.5` (octile) of that point: zero
   force, return. Arrived creatures are not pushed apart.
2. Asks the ungated enumerator (`[0x006ED3FC]`, context `[0x006ED40C]`) for
   neighbours within `1.2 x radius` and runs the callback on each, summing into
   `[0x006ECAD8]`/`[0x006ECADC]`.
3. Adds one more repulsion, from a **remembered entity**: the id at
   `entity+0x537`, written with the countdown at `+0x539` by the victim
   selector `0x0045CB00` and resolved to a body by `0x00460F60`. With
   `v = pos - other.pos` and `d = octile(v)`, the reach is that entity's
   `DistanceCanSee` in whole tiles (definition `+0x77B`, `(KWD 0xEC8 + 0x800)
   >> 12`, 4 for every shipped creature) plus own `radius`, or one tile plus
   `radius` while the body's state is 8 (en route). Within reach:
   `r = (d << 16) / reach`, magnitude `max_force` at `r <= 0x28F` else
   `2^32 / r - 1.0`, and `normalise(v) x magnitude` is added. **The entity is the
   one it fights** (`combat-target-selection.md` §5, which reads the writer).

**The callback**, for a body A and a neighbour B:

| step | what the original does |
|---|---|
| skip | B is A; or A's `is_blocker` (`+0x10E` bit 7) |
| offset | `v = A.pos - B.pos`, `d = octile(v)`, `n = normalise(v)` |
| gate | a push only while `0 < d <= rA + rB` |
| ratio | `r = (d << 16) / (rA + rB)` |
| arm 1 | `r <= 0x28F` (about 1 %) -> magnitude is A's `max_force` |
| arm 2 | B `is_blocker` and A not `ignore_blocker` -> `2^32 / r - 1.0` |
| arm 3 | otherwise `root = isqrt(r)`; `root = 0` -> `max_force`, else `2^32 / root - 1.0` |
| cosine | `c = n . normalise(A.vel)`; `k = (c + 1.0) / 2` |
| size | `F = table[B.size - 1][A.size - 1]`: `B / (A + B)` over size classes 1-5 (definition `+0x624`, KWD `0xEA8`; Imp 1, Bile Demon and Giant 5), except cell (A 4, B 5), which holds 0.75 |
| scale | `S = (k + 0.2) x F` as a **plain 32-bit multiply with no `>> 16`** |
| store | `accumulator += (n x magnitude) x S`, a 16.16 multiply |

The unshifted multiply makes `S` some 65536 times a 16.16 product, and the
store's 16.16 multiply then keeps bits 16-47 of `(n x magnitude) x S`, which
wraps again for any real contact, per component. So the stored contribution
is the **middle bits of two overflowing products**: deterministic in the
operands, unrelated to the geometry (a stationary equal-size contact comes out
pointing *toward* the neighbour; the sign of `S` decides nothing). The combiner
then truncates it to `max_force`. Arithmetic CONFIRMED from the binary by two
readers; what contact looks like in play is unobserved (Q345). Priority (4):
reproduce, do not fix.

`isqrt` is `0x0065FAE0`: seed from the highest set bit out of a 32-entry table at
`0x0065FB14` holding round(2^(i/2)), then Newton-halve until the quotient stops
being smaller. It returns the first converged guess and that is what the curve
is fed.

**What is built:** `SeparationRules` holds the arms, the size table and the
scale as arithmetic; `SeparationBehaviour` assembles the force (ADR-0156,
wired by ADR-0157 phase 3).

### 3.3 How the forces combine (Q343), and what that settles

The eight singletons are reachable only through a **pointer table of 8 entries at
`0x006A1488`**, in declaration order: seek, arrival, separation, avoid_obstacle,
repel_obstacle, formation, avoid_walls, follow_path.

`FUN_004D56E0` is the only function that reads it. It looks four behaviours up
**by name**, with an inline string compare against the literal each singleton
carries, and pushes an 8-byte `{weight, behaviour}` pair onto a list whose base,
top and limit are `0x006ECAE0`, `0x006ECAE4` and `0x006ECAE8`:

| behaviour | weight | in 16.16 |
|---|---|---|
| `avoid_walls` | `0x9999` | **0.6** |
| `repel_obstacle` | `0x6666` | **0.4** |
| `follow_path` | `0x5999` | **0.35** |
| `separation` | `0x2147` | **0.13** |

**`avoid_obstacle` is never registered**, though its method is real. Of eight
behaviours, three are stubs (§3.2), one is implemented but unused, and **four are
combined**.

`FUN_004D4760` consumes the list once per body, and the loop is short:

```
for each {weight, behaviour} in the list:
    f = behaviour->update(body, arg)      ; the virtual, out-parameter first
    f = truncate(f, body.max_force)       ; 0x004D4F60
    total += f * weight                   ; 0x004D1E70, 16.16
```

So the combination is a **weighted sum of individually clamped forces**. Because
each term is clamped before weighting and addition commutes, the list's order does
not affect the result.

`0x004D4F60` is a vector truncation: it takes the true Euclidean length through
`isqrt(x*x + y*y)` — in three precision bands, shifting the inputs right by 0, 8 or
18 and the root back again, so the square never overflows — and rescales to the
limit only when the length reaches it.

**This settles Q346.** Separation's saturation arm looked like it undercut the
curve beside it by orders of magnitude. It does not, because nothing downstream
sees either number raw: every behaviour's force is truncated to `max_force` before
it is weighted. The curve arm's very large magnitude is clamped to `max_force`, and
the saturation arm returns `max_force` directly — the same value by a shorter road.
The discontinuity is in the magnitude, not in the force.

What consumes `total` is §3.4; the walk over the twelve-slot list is the slide
along solid obstacles, `steering-behaviours.md` §4.

### 3.4 The constants are hard-coded, and the integration is Euler

`FUN_004D5B30` resets a steering body and writes these **literal defaults**:

| field | offset | literal | 16.16 |
|---|---|---|---|
| `radius` | `+0xDC` (word) | `0x3333` | **0.2** |
| `max_force` | `+0xDE` | `0x18000` | **1.5** |
| `max_speed` | `+0xE2` | `0x6666` | **0.4** |

Body `+0xDC` is creature `+0x200` (body starts at `+0x124`), also consumed by the crowd probe.
After reset, `0x004872A0` replaces radius with `(FootprintRadius >> 1) << 4` via `0x004B5D60` at `0x00487B0B`, and speed via its getter.
Radii therefore vary by definition: Dwarf 819 becomes `0x1990`, about 0.1 tile. The hostile-door branch of `0x00488930` resets again at `0x0048A422`.
Our per-tick footprint assignment does not reproduce that later reset's `0x3333` radius; ordinary radius is correct.

The tail of `FUN_004D4760` is the integration, and it clamps **three** times --
`0x004D4F60` has exactly three call sites, all in this function:

```
total = sum over behaviours of truncate(f_i, max_force) * weight_i   ; clamp 1, per force
total = truncate(total, max_force)                                   ; clamp 2, on the sum
accel = total / 0x10000                                              ; 0x004D1EC0, mass = 1.0
vel   = vel + accel                                                  ; 0x004D1E10, into +0x8C
vel   = truncate(vel, max_speed)                                     ; clamp 3, on velocity
pos   = pos + vel                                                    ; 0x004D1E10, into +0x84
```

So it is plain Euler integration of a force into a velocity, with the force
bounded twice and the velocity once. `0x004D1EC0` is a vector divide by a 16.16
scalar; it is handed `0x10000`, which is a division by a mass of exactly 1.

The position step has one guard. When `path_flags` bit 2 is set and the mover
is on its **last** waypoint, it compares the octile distance still to go with
the octile length of this tick's `vel`; if the step would overshoot, the new
position is **snapped onto the waypoint and `vel` is zeroed**. So a creature
lands exactly where it was going rather than oscillating around it.

**This is what wiring `SeparationRules` was waiting on, and it is now read end
to end.** What remains is not a gap in the original but a decision about ours:
whether a tile-to-tile mover can host a velocity at all without ADR-0155's
larger decision being taken first.

## 4. The per-creature path state is the steering body

It is not in the behaviour. It is the object
[debug-dumpers.md](debug-dumpers.md#the-steering-body--0x004d5c0f) already
recovered from dumper `0x004D5C0F`, whose lower-case field names are the
original's own: `pos`, `vel`, `local_xaxis`, `local_yaxis`, `path_target_pos`,
`projection_vector`, `radius`, `max_force`, `max_speed`, and the `path_*`
block at `+0xFA`-`+0x110`.

Two of its fields are read directly by `0x004D3C40` — `path_total_dist`
`+0xFA` and the `path_finished` bit at `+0x10E` bit 6 — which confirms that
dumper's object and the behaviour's argument are the same thing.

`path_buffer_count` beside `path_total_count`, and `subpath_buffer` beside
`path_total_dist`, say the mover holds **a window of the path rather than all of
it**, refilled as it advances. That is consistent with `Route2path` being a
stage that can be re-run, and with the two separate "exceeded max path length"
limits.

## 5. The mesh is built, not shipped

No `.pt` or `.tr` file exists anywhere in the reference installation. The
triangulation is therefore constructed at runtime from the map, and those two
filenames are a developer dump-and-reload facility. `nav.log` is referenced from
19 sites in the same module, which is consistent with the whole subsystem
carrying a logging build.

**It is live.** The route entry `0x004D0E10` is reached from five callers, and
one of them, `0x004D0E60`, has twenty callers spread across the creature and AI
code at `0x0045xxxx`-`0x0047xxxx`, including the `0x0045CB00` chain that
`0x00514830` drives (`creature-action-table.md` §4). This is the mover the game
actually uses, not a disabled development path.

## 6. What this means for our pathfinder

Since ADR-0157 phase 3 (2026-09-17) every creature walks this way:
`WorldSteering` keeps the navigation byte of every tile, rebuilds the mesh when
one changes and answers the four probes; a system that wants a creature
somewhere asks it for a route to a tile's centre, and `CreatureMovementSystem`
runs the mover's tick (steering-behaviours.md §7.5). `GridPathfinder` survives
only behind `IPathfinder.TryFindNearest`, which chooses work by walking
distance where DK2 chooses by kind lists and ring scans (ADR-0058); the route
to what it chose comes from the mesh, which skips what it cannot reach; since
ADR-0339 the grid also refuses the mesh's one gap, a corner-only pair (Q28).

The parts of ADR-0012 that were measured against the game still hold as
observations: a route runs straight where it can (Q341) and no terrain is
weighted (Q30).

## 7. What this leaves

- `0x004CFFD0` divides a 64-bit numerator by a 32-bit divisor, so near-parallel
  lines could fault the original; whether a layout reaches that is UNKNOWN.
  `GateWalk` reports no crossing there.
- On the funnel's last point `0x004CF490` reads one point past it — the previous
  route's leftovers. `GateWalk` uses a degenerate segment, which never crosses.

## Confidence

**CONFIRMED (binary):** `Follow_Path` is DKII.EXE's own class name, with the
descriptor, locator and vtable addresses given in §1; the family is `Behaviour`
plus exactly eight named subclasses; each is a global singleton constructed at
startup and holding only a vtable and a name; `Follow_Path`'s single virtual is
`0x004D3C40`, it reads `creature+0x370` and the mover's `+0xFA` and `+0x10E`
bit 6; the navigation module's diagnostic strings quoted in §2 are present; the
route entry's caller graph in §5.

**CONFIRMED (negative):** no `.pt` or `.tr` file ships in the reference install.

**CONFIRMED (binary + owner):** that a route is reduced to grid-free waypoints
by a funnel over gates, and that routes run straight where the funnel needs no
turn (§2.1, §3.1).

**CONFIRMED (binary):** that the corridor is found by a greedy best-first
search over a triangulated mesh, run from both ends with the shorter funnelled
result kept (Q341, `navigation-mesh-and-search.md`).

**CONFIRMED (binary):** the gate re-walk of §2.1 end to end — its eight cross
products, selection, cap, adjusters and intersection — and that the cap is the
wrapper's `0x1000`. UNKNOWN: whether a shipped map has a sixteen-tile gate.
