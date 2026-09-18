# Mechanic: the navigation mesh and the route search (Q341)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `structure-blocks.md` §5.4 for the navigation-cache byte the mesh is built from
- `CreatureEnums.cs` for the two flag names the cost function turns out to read

Original version: DK2 v1.70 (GOG)

Questions: **Q341** (the search), **Q347** (the triangulation) and **Q348**
(edge classes, constraint flags, region ids), all answered here at the algorithm
level. Continues `navigation-and-steering.md`, which owns the funnel (§2.1) and
the mover; this page owns everything before the funnel.

**Built** (ADR-0157 phases 1 and 3): `NavMesh`, `MeshRouter` and `Funnel` in
`DK2.Pathfinding`, driven by `WorldSteering` for every creature since 2026-09-17
and building all seven campaign maps.

---

## 1. The call chain

| stage | address | what it does |
|---|---|---|
| request | `0x004D0E60` | fills six per-query globals from the creature, passes positions `>> 4` and mode `-2` |
| dispatcher | `0x004D0E10` | adds the mover class (`[0x006A11F0] − 1` = 0 by default) and calls the orchestrator |
| orchestrator | `0x004D0320` | route cache; selects the passability table; runs the driver; then `Route2path` |
| driver | `0x004CD920` | forward search, funnel, backward search, funnel, keep the shorter |
| search | `0x004CD000` / `0x004CD490` | greedy best-first over triangles, one per direction |
| funnel | `0x004CEF40` | `navigation-and-steering.md` §2.1 |

Every stage is timed into `nav.log` under a ` c=%d %s` label when logging is on;
`Fwd route`, `Bak route`, `Fwd path`, `Bak path`, `Route2path`, `DOUBLE PATH` and
`EndGates` are those labels, not data. A request logs
`F=<counter> Path x0,y0 x1,y1` in 1/256 tile, then ` retC`, ` gate … EndGates`
(the mover's branch) or, for a reachability probe,
` route=<triangles> … way=<waypoints>`; ` Fwd route route=-1` is a failed search
and ` DOUBLE PATH` the cache. ` route=` is the driver's return, the kept
corridor's **last index** (`0x004CDE21` copies entries `route..0`), so it is
triangles − 1. A single-tile rebuild logs ` Area x0,y0 x1,y1`.

## 2. The mesh in memory

`0x004CE690(buffer, bytes)` — one caller, the navigation initialiser at
`0x004D1840` — carves **a single buffer** into every array the router uses. The
triangle capacity is `N = (bytes − 4) / 29`, and 29 is simply the per-triangle
sum of the slices:

| array | global | per triangle |
|---|---|---|
| triangles, 16 bytes each | `[0x006ECA0C]` | 16 |
| points, 4 bytes each, capacity `N/2` | `[0x006ECA10]`, count `[0x006ECA04]` | 2 |
| visited stamp | `[0x006ECA14]` | 1 |
| predecessor | `[0x006ECA18]` | 4 |
| search key | `[0x006ECA1C]` | 4 |
| route slots, capacity `N/4` | `[0x006ECA20]`, count `[0x006ECA08]` | 1 |

A **triangle** is three point indices (words at `+0`, `+2`, `+4`), three
neighbour triangle indices (`+6`, `+8`, `+0xA`), the **navigation-cache byte of
its tile** at `+0xC`, a byte of **per-edge flags** at `+0xD`, and a word at
`+0xE` holding three **2-bit edge width classes** below a ten-bit **region
id** (§2.2). A **point** is two words, `x` then `y`.

The mesh is **built, not shipped**. `0x004CEF20` calls the re-triangulation entry
`0x004CE770` with the whole `256 × 256` map, and the notifying nav-byte setter
`0x004D1930` (§5.4) calls it with the one changed tile. `0x004CE770` normalises
the rectangle and has single-tile and whole-map fast paths; what it does inside
is §2.1. `default.pt` / `default.tr` are written when the search's heap
overflows — a diagnostic dump of the mesh, which is why no such file ships.

### 2.1 How the mesh is built (Q347)

The triangulation is **constrained and not Delaunay**, and it is driven by
rectangles, which is the whole reason routes cross rooms in one straight line.

**Reset** (`0x004C8D10`, whole-map builds only): four corner points and **two
triangles** over a fixed canvas of **-256 to 1280 tiles** (`0x004C8F30`), far
outside any map, both stamped `+0xC = 0x0F`; every other slot is marked dead
with `+0xC = 0xFF`. The two are stored as `(3,1,0)` and `(1,3,2)` over corners
`0..3`: **clockwise** in the cross product's terms, where our builder winds the
other way. Only the edge function's endpoint order sees the difference, and
`GateWalk` allows for it. The whole-map path then inserts **the map itself as
one rectangle of navigation byte 0** (`0x004CE99D`), so the map border is an
ordinary constraint and everything past it keeps `0x0F`, which is solid.

**Decompose.** `0x004CE570` scans the rebuild rectangle for **maximal uniform
rectangles** of one navigation byte: from a start tile it extends right while
the bytes match, then extends down while each whole row segment matches, and
returns the rectangle with its byte (`0x004CE4E0` starts at the topmost
uncovered row's leftmost run, a per-column skyline at `0x006E7EE8` recording
cover, so the partition is the one ours makes). The rebuild loop asks for the next one
until none remain.

**Insert each rectangle** (`0x004CCCB0`): its four corners as points, its four
sides as constraint segments, then stamp the triangles inside it with the
rectangle's navigation byte (`0x004CC9B0`). A whole-map rebuild **skips every
rectangle whose byte is 0** (`0x004CEB56`), because the base rectangle already
stamped it; that alone accounts for the whole point-set difference of §7.

- *Point insertion* `0x004CAA50(x, y)`: locate the containing triangle
  (`0x004C98B0`, inputs scaled `<< 8`); if the point is already a vertex do
  nothing; if it lies on an edge (orientation test = 0) split the **two**
  triangles sharing that edge into four; otherwise `0x004CA500` splits the one
  triangle into three. New triangles inherit the parent's `+0xC`. There is
  **no in-circle test and no legalisation** anywhere in this path.
- *Constraint insertion* `0x004CBD10(x0, y0, x1, y1)`: locate both ends, then
  walk from one to the other along the segment using signed cross-product
  orientation tests, **flipping every triangulation edge the segment crosses**
  (`0x004C9FC0`: two orientation tests as a convexity guard, then both
  triangles' vertex and neighbour words are rewritten). If the segment passes
  exactly through an existing vertex the routine **recurses on the two halves**.
  Its flip order is unread; ours collects the crossed edges first and flips them
  in Sloan's queue order — a non-convex quad waits, a new diagonal that still
  crosses re-queues — because flipping the first crossed edge and restarting
  cycled on five campaign maps.

**Coarsen** (`0x004CC650`, after each rectangle and once more at the end): a
flood over the rebuilt area stamped by a generation counter, which **never
enters a solid triangle** (bit 3 of `+0xC`) and abandons the pass once its stack
passes 1000. Each triangle's three edges go to `0x004CC190`, and a yes flips
(`0x004C9FC0`). The criterion is that both triangles carry **one navigation
byte**, bit `edge` of `+0xD` is set on both, the quad is **convex** (two
orientation tests), and its other diagonal is **strictly shorter** in squared
length. The mesh is therefore a **shortest-diagonal** triangulation of each
uniform region, not a Delaunay one, and rock keeps whatever insertion left. This
is what holds a whole room to a handful of triangles (Q351).

**Removing a vertex is not part of a whole-map rebuild.** That pass is gated on
the partial-rebuild flag (`0x004CEC10`) and sees only the points along the
rebuilt rectangle's four sides (`0x004CE410`). For each, `0x004CAF40` walks the
fan flipping incident edges until the degree is 3 or 4, then `0x004CB8C0` (3) or
`0x004CB170` (4) frees the point (`+2 = 0x8000` marks a dead point) and pushes
the dead triangles onto a free list threaded through `+6`. A whole-map build
keeps **every** rectangle corner it inserted, which is what §7's dump shows.

**Bookkeeping.** Every new or rewired triangle passes through `0x004C8F50`,
which computes its three edge width classes, and a rebuild ends by
re-labelling connected regions. Both are §2.2.

Point coordinates are 16-bit words at tile-corner positions; a tile edge that
separates two different navigation bytes is always a constraint edge, and a run
of tiles with one byte never is. That, and not anything about Delaunay
quality, is what decides where the funnel's gates fall.

### 2.3 A route endpoint on a corner or an edge leans south-east (Q354)

The orchestrator locates both endpoints through `0x004C9DF0` (calls at
`0x004D06CE`, `0x004D06DD`). It first walks the mesh (`0x004C98B0`: start at a
4×4 per-cell cache of the last answer, then cross whichever edge has the point
strictly outside; a point on an edge or vertex counts as inside, so the walk
stops at the first triangle whose closed boundary holds it). Then it corrects
the tie. If the point **is a vertex**, `0x004C9540` turns around that vertex,
crossing the edge that leaves it, until the far point `(0x10100, 0x10100)`
— 257 tiles past the origin on both axes — lies strictly inside the edge
arriving at the vertex and inside or on the edge leaving it; off the hull it
returns −1. If the point lies **on an edge** and the far point is outside
that edge, the neighbour across is taken. So a target on the corner of a solid
tile belongs to the tile to its south-east: floor there routes, rock there is
refused. Ours is `NavMesh.LocateEndpoint`, written for our winding, which is
the original's reversed (§2.1's reset).

### 2.2 Edge width classes, constraint flags and region ids (Q348)

Three per-triangle annotations, each now read to its consumers.

**`+0xE` low six bits: a 2-bit width class per edge.** `0x004C8F50` runs after
every triangle is created or rewired. For each edge it takes `|dx|` and `|dy|`
between the endpoints, clamps both to 3, and looks up the 4x4 table at
`0x66DA38`, which is simply `max(|dx|, |dy|)` clamped to 1..3 — how many tiles
the edge spans; the three results are packed two bits each. The search (§4)
indexes its passability table with `(classes & 0x3F) | mask[slot]`, the masks
being `0x3C`, `0x33`, `0x0F` for the three neighbour slots, so each lookup sees
the class of **the edge being crossed**. Mover class 1 passes only edges of
class 2 or 3 (at least two tiles wide); class 2 only class 3.

**It is a clearance mechanism, and the shipped game never engages it.** The
class byte reaches the orchestrator only through the dispatcher, as
`[0x006A11F0] − 1`; that byte initialises to 1, has no writer and sits in no
block copy. Every request uses the all-ones table, and no gate is ever too
narrow. A live mechanism with a zero rate, not dead code.

**`+0xD`: two flags per edge.** Bits 3–5 mark an edge as a **constraint**:
`0x004CE380` sets bit `slot + 3` along each inserted rectangle's sides, the
clone helper clears it for a new internal edge, and the flip `0x004C9FC0`
tests bit `slot + 3` on both triangles and **refuses** if either is set. Only a
partial rebuild calls the side marker, so a whole-map build carries no
constraint at all and the byte test alone keeps category boundaries; a Level 10
dump has `+0xD = 7` on all 2,828 triangles. Bits 0–2 are set on every new edge
and read only by the coarsening criterion.

**`+0xE` high ten bits: a connected-region id.** `0x004C91A0` is a breadth-first
flood fill over non-solid triangles (bit 3 of `+0xC` clear) with a 200-slot
ring queue, assigning ids and keeping 3-byte per-region records at `0x6E96C8`
(a word count and a byte). `0x004CE410` gathers the ids bordering a rebuilt
rectangle so the fill can be re-run locally. Outside the builder only the
orchestrator reads them: `0x004D0320` asks `0x004C9400` to compare the two
endpoint triangles' ids before either mode's search runs (§5); the search itself
never consults them. When neither region is marked in use the check re-runs the
fill and compares the two words with `XOR` and `AND AL,0xC0` (`0x004C952B`) —
the low two bits of the id only, so two regions whose ids differ by a multiple
of four read as one. Rare in play: ids are almost always in use.

## 3. The search is greedy best-first

`0x004CD000` (the other direction is `0x004CD490`, same shape) is a **min-heap
search over triangle indices** with:

- a **1024-entry heap** at `0x006EB9A0`; overflow prints
  `Heap Overflow (Triangles %d, Map %d*%d)` and dumps the mesh;
- a **visited stamp** per triangle — a byte at `[0x006A11E8]` incremented per
  search, the array cleared when it wraps, the same generation trick our own
  `GridPathfinder` uses;
- a **predecessor** per triangle, read back into the route slots on success;
- **two pops a round** (`0x004CD15A`–`0x004CD1C4`): the root, then the next
  root unless it is the goal, both expanded before the root is looked at again;
- termination when the goal triangle is at the **root**.

The key stored for a neighbour `n`, and the only thing the heap compares
(`0x004CCF00` sifts on `[0x006ECA1C]` with a `−1` sentinel at the root), is

```
key(n) = ( min over n's three vertices of (dx² + dy²) to the goal ) << (2·cost − 2)
```

where `cost` is §4's transition cost, 1–4. **There is no accumulated distance
from the start anywhere in the key.** This is not Dijkstra and not A*; it is
greedy best-first on squared straight-line distance, and it will take a longer
corridor whenever a closer-looking triangle leads into a dead end. Two things
soften that: the visited stamps make it complete on a finite mesh, and §3.1 runs
it twice.

### 3.1 Two directions, shorter wins

The driver runs the search from one end, funnels the result, runs it from the
other end, funnels that, and at `0x004CDDF3` **compares the two funnelled
lengths and keeps the shorter**. If the backward one wins, its waypoints are
copied into the forward buffer reversed — that is `Bak reverse choice`. If
either search fails the request fails; the two are not fallbacks for each other.

Which end seeds the forward search is STRONGLY INFERRED from argument order to be
the *destination*, so that the predecessor chain unwinds from the creature. It
changes nothing about the route.

## 4. Passability and cost

Two gates decide whether the search may step from triangle `c` into `n`.

**The class table.** `[0x006E5474]` points at one of the 64-entry tables at
`0x006E8AA8 + (class + 1) · 0x100`, selected by the request's class byte; the
`+1` means the default class 0 gets the table at `0x006E8BA8`, which is **all
ones**. Class 1's table passes only flag bytes with bit 1 set; class 2's only
those with bits 0 and 1. The index is `(c.classes & 0x3F) | mask[slot]`, with masks
`0x3C`, `0x33`, `0x0F` for the three neighbour slots — not one mask per search
direction, as an earlier draft of this page had it — so each lookup sees the
width class of the edge being crossed (§2.2). In the shipped game the class
byte is always 0 and the table always all ones.

**The cost function `0x004C8BD0(from, to)`**, on the navigation bytes of the
tile left and the tile walked into (`8` solid, `1` and `3` two terrain
categories; a closed door's byte is its owner in the high nibble, bit 2 when
its lock mode is not zero and bit 1 from the tile's flag bit 0, the task pool's
"tagged" bit — doors-and-traps.md). The two searches push the arguments in
opposite orders (`0x004CD233`, `0x004CD6C8`) so that `to` is always the tile
walked into: seeded at the destination, that is the popped triangle, not the
neighbour. In order, with `n` the tile walked into and `c` the tile left:

| arm | result |
|---|---|
| `n` has bit 3 (solid) | 0 — impassable |
| `n` is a door (owner nibble set) of another player with bit 1 | 0 |
| `n` is a door, with `[0x006EC9E0]` (option bit 7, "respect doors") | 0 for another player's door, and for this player's with bit 2 |
| `n` is a door, without it and without `[0x006EC9D4]` (option bit 1) | 0 for this player's door with bit 2 |
| `n` is not a door but `c` is | the same two tests on `c` |
| `n` is category **3** and `c` is not, unless `[0x006EC9DC]` | 0 |
| `n` is category **1** and `c` is not, unless `[0x006EC9E4]` | 0 |
| bit 2 differs between `n` and `c`, unless `[0x006EC9D8]` (option bit 2) | 0 — on a plain tile bit 2 is a **portal's centre hole**: the captured cache holds 4 on exactly Level 7's two portal centres, so only a state passing option bit 2 (leaving, 0x6E) walks in |
| otherwise | **`(n.nav & 3) + 1`**, so 1, 2, 3 or 4 |

The requests (`0x004D0F60` for the mover, `0x004D0E60` and `0x004D1700` for
reachability) set those globals from the creature: the player id from
`[creature+0x2A] + 0x10FC`; **`[0x006EC9E4]` from definition flags `def+0x686`
bit 4 and `[0x006EC9DC]` from bit 5** through the compact definition at
`creature+0x370`. Our `CreatureFlags` names those bits **`CanWalkOnWater`**
and **`CanWalkOnLava`**. So navigation category **1 is water and 3 is lava**,
each impassable to a creature without the flag. The three door globals come
from the state's option word (steering-behaviours.md §7.6): bit 1 →
`[0x006EC9D4]`, bit 2 → `[0x006EC9D8]`, bit 7 → `[0x006EC9E0]`, parked in
`creature+0x3E6` bits 17–19 for the call. Most *states* pass `0x80`, so a
creature normally refuses every door it does not own and its own locked ones —
but most *requests* in a running game do not, because two thirds of them are the
imp pass, and an imp en route passes `0x00` (navigation-captures.md, Q357).

**What "cost" does.** It never accumulates. It shifts the greedy key: floor ×1,
water ×4, lava ×64. That is a bias in *expansion order* — the search tries
floor-looking triangles first — not a path cost. **Q30** (no terrain weighting;
the shortest route wins even through water, CONFIRMED by observation) is not
contradicted: a creature that may enter water routes through it, and in open
water the bias changes nothing visible. Whether any layout exists where it
does is untested and now recorded against Q30.

## 5. The request and the route cache

`0x004D0E60(creature, target, options)` passes `creature+0x1A/+0x1E` and the
target, each `>> 4`, into a 2068-byte path result on its own stack, and returns
whether the result's count is non-zero. It passes mode `−2`, which permits the
**route cache**: the orchestrator compares the request against the last one —
both endpoints at `[0x006E5B00]`/`[0x006E5B04]` and `[0x006E5AB8]`/`[0x006E5ABC]`,
the class byte at `[0x006A11F4]`, and a dirty flag at `[0x006A11EC]` that the
notifying nav-byte setter raises through `0x004CE770` — and if all match, skips
both searches and re-runs only the funnel. `DOUBLE PATH` labels that path.

Every request that misses the cache first asks `0x004C9400` (one call site,
`0x004D06F1` in `0x004D0320`, before the mode branch) whether the two endpoint
triangles carry one region id, and returns a zero-length route (` retC`) if
not — the only reader of region ids outside the builder. The mover then sets
`path_finished` and `Follow_Path` brakes; nothing retries, digs or walks
straight at the target. Our router has no region ids and relies on the search
failing instead. §1's labels are gated on `[0x006EC9E8]`, which 44 instructions
read and none writes (§7).

## 6. What this leaves

- What bits 0–2 of `+0xD` mean; they are set everywhere in a finished mesh.
- The seeding of the coarsening flood (`0x004CC3D0`).
- The point-location walk's start cache refresh (`0x004C97B0`) and `0x004C9B50`.
- The triangle-index list at `0x6E86F0`, pre-marked visited by every search and
  consulted by `0x004C9710` when it looks for a boundary edge.
- The door matrix in the cost function's third arm.
- Why a backward search failing after a forward success returns no route.

## 7. Measured against the original's own log

`navigation-captures.md` is the log of those captures. In short: replaying
Level 7's 21,298 requests on DK2's own dumped navigation bytes, **21,288 agree**
(99.95 %); on a Level 10 played by a keeper, **every request but three of
86,140** is explained by one of six mover profiles, corridor lengths are exact
in 87 %, and at tick 7 DK2's point set is **exactly ours**, 1,417 for 1,417, with
2,612 of its 2,765 triangles identical. **One** request of 20,926 produces a
waypoint count no mover profile reproduces. What is left is the corridor: ours
is one or two triangles short of DK2's on 12 % of requests, which Q351 puts on
the quads whose two diagonals tie.

## Confidence

**CONFIRMED (capture, 2026-09-17 and 2026-09-18):** §7's agreement rates, and
the door arms of §4, which reproduce DK2's own `route=` and `way=` once each
request is scored against the option word its caller passes (Q357). **CONFIRMED (binary):** the call chain and every address in §1; the allocator's
layout and the 29-byte derivation; the triangle and point records as far as the
fields named; the full-map and single-tile rebuild entries; the heap, its cap,
the stamp, the key expression and that the heap compares on it; termination on
pop; two directions with the shorter funnelled length kept; the class-table
selection with its `+1`; every arm of the cost function as tabled; the six
globals and their sources; the route cache and its keys.

**CONFIRMED (binary):** the reset to two triangles; the maximal-rectangle
scan; four corners then four constraint sides per rectangle; point insertion
as on-vertex / on-edge / interior with no in-circle test; constraint insertion
by orientation-guided flipping with recursion at vertices; the flip itself;
stamping of `+0xC`; the base rectangle and the byte-0 skip; the coarsening
flood, its criterion and its exemption of rock; that the vertex-removal pass is
gated on the partial-rebuild flag; the merge, the dead markers and the free
lists; that `+0xE` is a word with six flag bits below a ten-bit id.

**CONFIRMED (binary):** the width-class table and its packing; the three
per-slot masks and that the search advances through them; that the class byte
is `[0x006A11F0] − 1` with no writer of that byte found; the constraint bits
`slot + 3` — set by the side-marker, cleared by the clone, refused by the flip;
the flood fill, its queue and its records; that outside the builder only the
orchestrator's pre-check `0x004C9400` reads region ids, before either mode's
search (§5); the search's two pops a round.

**CONFIRMED (binary + repo):** categories 1 and 3 are water and lava — the two
definition bits the request reads are the two our `CreatureFlags` already names.

**STRONGLY INFERRED:** that the forward search is seeded at the destination;
that the width-class unit is tiles.

**UNKNOWN:** everything in §6.
