# Mechanic: the navigation mesh and the route search (Q341)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `structure-blocks.md` §5.4 for the navigation-cache byte the mesh is built from
- `CreatureEnums.cs` for the two flag names the cost function turns out to read

Original version: DK2 v1.70 (GOG)

Questions: **Q341** (the search) and **Q347** (the triangulation), both answered
here at the algorithm level; **Q348** carries the flag bits and region ids.
Continues `navigation-and-steering.md`, which owns the funnel (§2.1) and the
mover; this page owns everything before the funnel.

**Nothing here is built.** `GridPathfinder` is still the router (ADR-0155).

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
`Fwd route`, `Bak route`, `Fwd path`, `Bak path`, `Route2path`, `DOUBLE PATH`
and `EndGates` are those labels, not data.

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
its tile** at `+0xC`, and a word at `+0xE` holding six flag bits below a
ten-bit id (§2.1). `+0xD` is not read by anything on this page. A **point** is two words, `x` then `y`.

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
triangles** covering the map, every other slot marked dead with `+0xC = 0xFF`.
Everything else is inserted incrementally.

**Decompose.** `0x004CE570` scans the rebuild rectangle for **maximal uniform
rectangles** of one navigation byte: from a start tile it extends right while
the bytes match, then extends down while each whole row segment matches, and
returns the rectangle with its byte. The rebuild loop asks for the next one
until none remain.

**Insert each rectangle** (`0x004CCCB0`): its four corners as points, its four
sides as constraint segments, then stamp the triangles inside it with the
rectangle's navigation byte (`0x004CC9B0`).

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
  That is textbook constrained-edge insertion by flipping.

**Coarsen.** Adjacent triangles whose `+0xC` bytes agree are merge candidates
(`0x004C97B0` compares neighbours); `0x004CB8C0` merges two across a shared
edge, frees the vertex between them (`+2 = 0x8000` marks a dead point) and
pushes both dead triangles onto a free list threaded through their `+6` slot.
This is what keeps a whole room down to a handful of triangles.

**Bookkeeping.** `+0xE` is a **word**: the low six bits are flags set two per
edge as triangles are cloned, and the high ten bits are an id whose per-id
triangle counts live at `0x6E96C8`, with a byte table at `0x6E96CA` beside
them. Which functions among `0x004CE0C0`, `0x004CE380`, `0x004CE410`,
`0x004CC650` and `0x004CB170` perform the coarsening and the id assignment is
STRONGLY INFERRED from their calls rather than read. What the six flag bits
*mean*, and whether the id is a connected-region label (which would make
reachability a single compare), is **Q348**.

Point coordinates are 16-bit words at tile-corner positions; a tile edge that
separates two different navigation bytes is always a constraint edge, and a run
of tiles with one byte never is. That, and not anything about Delaunay
quality, is what decides where the funnel's gates fall.

## 3. The search is greedy best-first

`0x004CD000` (the other direction is `0x004CD490`, same shape) is a **min-heap
search over triangle indices** with:

- a **1024-entry heap** at `0x006EB9A0`; overflow prints
  `Heap Overflow (Triangles %d, Map %d*%d)` and dumps the mesh;
- a **visited stamp** per triangle — a byte at `[0x006A11E8]` incremented per
  search, the array cleared when it wraps, the same generation trick our own
  `GridPathfinder` uses;
- a **predecessor** per triangle, read back into the route slots on success;
- termination when the goal triangle is **popped**.

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
those with bits 0 and 1. The index is `(n.flags & 0x3F) | 0x3C` in the forward
search and `| 0x33` in the backward one, so each variant tests a different
*pair* of the six flag bits. What the pairs mean is Q347.

**The cost function `0x004C8BD0(n.nav, c.nav)`**, on the two tiles' navigation
bytes (§5.4: `8` solid, `1` and `3` two terrain categories, owner in the high
nibble and two state bits for doors). In order:

| arm | result |
|---|---|
| `n` has bit 3 (solid) | 0 — impassable |
| `n` is a door (owner nibble set) of another player with bit 1 | 0 |
| `n` is a door of this player, gated by two request flags and bit 2 | 0 on some combinations; the matrix is read but not summarised here |
| `n` is category **3** and `c` is not, unless `[0x006EC9DC]` | 0 |
| `n` is category **1** and `c` is not, unless `[0x006EC9E4]` | 0 |
| bit 2 differs between `n` and `c`, unless `[0x006EC9D8]` | 0 |
| otherwise | **`(n.nav & 3) + 1`**, so 1, 2, 3 or 4 |

The request at `0x004D0E60` sets those globals from the creature: the player id
from `[creature+0x2A] + 0x10FC`; **`[0x006EC9E4]` from definition flags
`def+0x686` bit 4 and `[0x006EC9DC]` from bit 5** through the compact definition
at `creature+0x370`. Our `CreatureFlags` names those bits **`CanWalkOnWater`**
and **`CanWalkOnLava`**. So navigation category **1 is water and 3 is lava**,
each impassable to a creature without the flag, and the three door globals come
from the request's option bits, parked in `creature+0x3E6` bits 17–19 for the
duration of the call.

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

## 6. What this leaves (Q348)

- The six edge-flag bits and the ten-bit id at `+0xE` (§2.1), and which of the
  five unread functions coarsens and which assigns ids.
- The point-location walk (`0x004C98B0`, `0x004C9B50`, `0x004C9540`).
- The list of triangle indices at `0x6E86F0`, pre-marked visited by every
  search and consulted by `0x004C9710` when it looks for a boundary edge.
- The door matrix in the cost function's third arm.
- Why a backward search failing after a forward success returns no route.

## Confidence

**CONFIRMED (binary):** the call chain and every address in §1; the allocator's
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
stamping of `+0xC`; the merge, the dead markers and the free lists; that
`+0xE` is a word with six flag bits below a ten-bit id.

**CONFIRMED (binary + repo):** categories 1 and 3 are water and lava — the two
definition bits the request reads are the two our `CreatureFlags` already names.

**STRONGLY INFERRED:** that the forward search is seeded at the destination;
which of the five unread functions coarsens and which assigns ids.

**UNKNOWN:** everything in §6.
