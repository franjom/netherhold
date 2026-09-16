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
`Formation`, `Avoid_Walls`, `Follow_Path`.

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
