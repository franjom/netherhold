# Mechanic: how DK2 routes and how it moves (Q5)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The binary's own RTTI class names and debug format strings, which are
  DKII.EXE's identifiers rather than anyone else's (§1)

Original version: DK2 v1.70 (GOG)

Questions: **Q5** (answered at the architectural level here; the algorithms
themselves remain open as Q341-Q344).

**Nothing here is built.** `GridPathfinder` is unchanged and is now known to be
structurally unlike the original (§6).

---

## 1. First, a provenance correction

Six documents in this repository say Flame "names a 36-byte `Follow_Path`
struct". That attribution is wrong, and it has quietly made the name look
riskier to use than it is.

`Follow_Path` is **DKII.EXE's own class name**, recoverable from the shipped
executable with no third-party artefact involved. Its MSVC type descriptor sits
at `0x006A15D8`, its complete object locator at `0x0067DBC8`, and its vtable at
`0x0066DB74`. The constructor additionally copies the lower-case literal
`follow_path` into the object (§3).

Flame knew the name because the binary contains it. So does anyone who opens the
binary. ADR-0080's rule is unaffected in substance — it is about Flame's
identifiers — but this particular name never needed it.

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
| `grtc:Exceeded max path length` | a third, separately named, length-limited stage |
| `NavigationSystem:`, `NavigationSystemMap:` | the module's own two-layer naming |

The shape is the standard one for mesh navigation: search the mesh for a
**route** (a sequence of adjacent triangles and the gates between them), then
run `Route2path` to reduce that corridor to a **path** of waypoints.

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

What would separate them is a destination **well off any multiple of 45
degrees** — ten tiles across and three up, over open floor. A grid route bends,
because it is made of 45 and 90 degree legs. A corridor reduced by `Route2path`
is one straight line at about 17 degrees. The bend is the tell, not the
smoothness. Recorded in `observation-checklist.md`.

### 3.2 Three of the eight do nothing, and Separation is read

`seek`, `arrival` and `formation` share one method, `0x004D2A40`, and it is a
**stub**: it writes a zero vector to the output and returns. Whatever those three
meant, the shipped game gets no force from them. Five behaviours are live.

`Separation` is `0x004D2DF0`, and its per-neighbour force is `0x004D2B80`. The
steering body sits at **`entity+0x124`**, which is how the two connect.

The outer routine:

1. Skips entirely while the body is within `radius + 0x8000` of its **final**
   waypoint, so creatures are not pushed apart once they have arrived. The
   waypoint is picked out of `subpath_buffer` at `+0xB4`, stride 8.
2. Otherwise queries neighbours within **1.2 x radius** (`0x13333` in 16.16,
   multiplied by `radius` at `+0xDC`), through an installed function pointer at
   `0x006ED3FC` with its context at `0x006ED40C`, passing `0x004D2B80` as the
   per-neighbour callback.

The callback, for a pushing body A and a neighbour B:

| step | what the original does |
|---|---|
| skip | B is A; or A's `is_blocker` (`+0x10E` bit 7) is set |
| offset | `v = A.pos - B.pos`, so the push is **away from** the neighbour |
| distance | `d = octile(v)` = longer axis plus half the shorter |
| gate | force only while `0 < d <= rA + rB` |
| ratio | `r = (d << 16) / (rA + rB)` |
| arm 1 | `r <= 0x28F` (about 1 %) -> magnitude is A's `max_force` (`+0xDE`) |
| arm 2 | B blocks and A does not ignore blockers -> `2^32 / r - 1.0` |
| arm 3 | otherwise -> `2^32 / isqrt(r) - 1.0` |

`isqrt` is `0x0065FAE0`: seed from the highest set bit out of a 32-entry table at
`0x0065FB14` holding round(2^(i/2)), then Newton-halve until the quotient stops
being smaller. It is reproduced rather than replaced, because it returns the
first converged guess and that is what the curve is fed.

**One thing does not reconcile.** Arm 1 is described by its own shape as a
saturation, yet for any ordinary `max_force` it yields *less* than arm 3 does
just above the threshold: at `r` one above `0x28F`, arm 3 gives about 171
million. Either `max_force` is far larger than it looks, or the curve's scale is
to be read differently, or the integration clamps the total anyway. Recorded as
**Q346** and pinned by a test rather than smoothed over.

**What is built:** `SeparationRules` reproduces all of the above as arithmetic,
tested, and **called by nothing** (ADR-0156). What stops it being wired is not
this force but the integration -- how the five live behaviours combine and
become motion (Q343) -- and our mover has no velocity to accept a force.

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

**Still UNKNOWN:** what consumes `total` — the step from an accumulated force to a
position, where `max_speed` (`+0xE2`) presumably enters — and the rest of
`FUN_004D4760`, which walks a count at `body+0x80` over 10-byte records from
`body+0x8` and compares squared distances against `0xCCC`. Neither is read.

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

`GridPathfinder` is eight-connected A* over tiles with octile cost. The original
searches a triangle mesh and then steers along the extracted path under forces.
These are not the same algorithm and they will not generally return the same
route, nor the same motion along it.

The parts of ADR-0012 that survive are the parts that were measured against the
game rather than assumed: eight-connectivity and corner-cutting (Q28,
CONFIRMED), no terrain weighting (Q30, CONFIRMED). Those remain true
*observations* and any replacement must still satisfy them.

What is now known to be wrong is the claim implied by the interface: that a tile
route is the thing DK2 computes. **This is an architectural decision and it is
the owner's**, so nothing has been changed. The options are recorded in
`open-questions.md` under Q341.

## 7. What this leaves

- **Q341** the route search itself: what the triangulation is built from, and
  what the two directions of `Fwd`/`Bak` actually are.
- **Q342** `Route2path`: which of the standard corridor-to-path reductions it is.
- **Q343** the steering integration: how the eight behaviours are combined, and
  with what weights.
- **Q344** `grtc:` — the third length-limited stage, unidentified.

## Confidence

**CONFIRMED (binary):** `Follow_Path` is DKII.EXE's own class name, with the
descriptor, locator and vtable addresses given in §1; the family is `Behaviour`
plus exactly eight named subclasses; each is a global singleton constructed at
startup and holding only a vtable and a name; `Follow_Path`'s single virtual is
`0x004D3C40`, it reads `creature+0x370` and the mover's `+0xFA` and `+0x10E`
bit 6; the navigation module's diagnostic strings quoted in §2 are present; the
route entry's caller graph in §5.

**CONFIRMED (negative):** no `.pt` or `.tr` file ships in the reference install.

**STRONGLY INFERRED:** that DK2 routes over a triangulated navigation mesh and
reduces a gate corridor to a waypoint path. Every string in §2 points this way
and nothing contradicts it, but no routine has yet been read end to end.

**HYPOTHESIS:** that `Fwd`/`Bak` mean a bidirectional search. They could equally
name the two directions of travel along one route. Q341.

**UNKNOWN:** the triangulation algorithm, the search, `Route2path`'s reduction,
the steering combination and its weights, and what `grtc:` is.
