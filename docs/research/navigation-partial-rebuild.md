# Mechanic: the navigation mesh's per-tile rebuild (Q591)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra on 2026-10-12. Addresses are facts about
  the binary in our own words; no decompiler output enters this repository (ADR-0046)
- [navigation-mesh-and-search.md](navigation-mesh-and-search.md) §2.1 for the whole-map
  build and the insertion, flip and scanner routines this reuses

Original version: DK2 v1.70 (GOG)

Questions: **Q591**. Built (ADR-0461).

---

## 1. What the binary does

Read from `0x004CE770` and its callees on 2026-10-12. Every changed navigation
byte reaches `0x004CE770` with that one tile (`0x004D1930`, navigation-mesh-and-search.md §2), and the mesh
is **edited in place, not rebuilt**. Ours edits in place too since ADR-0461
(`NavMesh.Retriangulate`, applied by `WorldSteering.Refresh` to each byte changed
since the last refresh); see "What ours does" below for what still differs.

**Which path.** `0x004CE770` sorts the rectangle's corners and returns on an
empty or `-1` one. A rectangle that does not keep one tile clear of every map
edge (`x0 < 1`, `y0 < 1`, `x1 + 1 >= width` or `y1 + 1 >= height`) is widened
to the **whole map** and takes the whole-map path (navigation-mesh-and-search.md
§2.1). The level's first build is one of those: its caller `0x004CEF20` passes
the whole map, `(0, 0, 0x100, 0x100)`. The first call ever also resets the
canvas, but that does not choose the path. Otherwise it takes the partial path,
with a single-tile flag when the rectangle is 1 × 1.

**The partial path, in order:**

1. **Cut the sides.** `0x004CDFE0` runs along each horizontal side and
   `0x004CDF20` along each vertical one. Each inserts a point (`0x004CAA50`) at
   both ends, plus one at every tile corner where the pair of bytes straddling
   the side changes. Then it inserts the whole side as one constraint
   (`0x004CBD10`).
2. **Mark them.** `0x004CE380` walks each side from vertex to vertex
   (`0x004C9C30` finds the triangle and edge leaving a point toward a target)
   and sets that edge's constraint bit `slot + 3` in `+0xD`.
3. **Empty the inside**, unless the rectangle is one tile. `0x004CE0C0` walks
   inward from the top side. It removes every vertex not on the rectangle's
   lines by `0x004CAF40` followed by `0x004CB170` or `0x004CB8C0`, clearing the
   region id of each triangle it passes. It stops at the constraint ring.
4. **Fill it.** The byte is the tile's own for one tile. For a rectangle under
   4 × 4 with one byte, it is that byte, inserted as one rectangle. Otherwise
   the skyline at `0x006E7EE8` is reset to the rectangle's top and the whole-map
   scanner partitions it. Unlike a whole-map build, **a byte-0 rectangle is
   inserted** too; the `0x004CEB56` skip applies only when the base rectangle
   was stamped. Each rectangle is followed by a coarsening pass over itself, and
   one more runs over the whole rectangle.
5. **Merge back.** `0x004CE410` walks the four sides again. It **clears** the
   constraint bits that step 2 set, clears each bordering region record's
   in-use byte (`0x006E96C8 + id × 3 + 2`), and
   queues every point it passes (`0x006E5478`, count `0x006ECA58`). Then, last
   queued first, `0x004C9B50` finds the point's triangle and vertex slot. The
   fan around it is walked while each neighbour carries the same byte. If the
   walk closes, so that the point has one byte all round, the point is
   redundant: `0x004CAF40` flips edges in its fan (`0x004C9FC0`, which refuses
   constraints and non-convex quads) until the degree is 3 or 4. Then
   `0x004CB8C0` (3) or `0x004CB170` (4) splices it out. A point on a byte
   boundary, or on the hull, stays.
6. **Hull.** `0x004C9710` finds a triangle with an open edge, from the cached
   hull list `0x006E86F0` or else by scanning every slot. The ring is then
   walked and the list rewritten.

**Slots are reused.** A removed triangle goes to the head of a free list
threaded through `+6` (head `0x006A11D4`): its byte becomes `0xFF` and the live
count `0x006ECA30` drops. The triangle allocators `0x004CA820` and
`0x004CA500` take the head of that list when it has one, and the next slot
past the high-water mark `0x006ECA2C` only when it is empty. So triangle
slots are reused last in, first out, and after the first edit, slot numbers,
and anything ordered by them, depend on the history of edits and not only on
the map. A removed point goes to its own list through `+0` (head
`0x006A11D8`, `+2 = 0x8000`, count `0x006ECA38`), and point insertion
(`0x004CAA50`, `0x004CA500`) pops that list the same way, before the
high-water mark `0x006ECA34`. The three-way split allocates its two
triangles before its point; the edge split allocates the point, then the
clone on the located triangle, then the clone on its neighbour. The removals
push the freed triangles in a fixed order (second then third for degree 3;
for degree 4, third then given on the first diagonal, second then fourth on
the other), so the later push is reused first.

**The flip moves constraint bits before it decides** (`0x004C9FC0`). After
refusing a constraint edge, and before its two convexity tests (the shared
edge's ends strictly on opposite sides of the line through the apexes), it
clears the constraint bit of each triangle's next edge and copies it across
to the other triangle's shared-edge slot, the bookkeeping a flip needs; the
stores at `0x004CA10D..0x004CA164` precede the tests. A flip refused as
non-convex therefore leaves those bits moved. The coarsening cannot reach
that refusal, since `0x004CC190` makes the same tests first; the degree
reduction `0x004CAF40` can, because it tries the flip speculatively around
a fan and steps on when refused, so in a partial rebuild a side's bit can
be displaced onto another edge while the inside is emptied or a side point
removed, where `0x004CE410` will not find it to clear. A whole-map build
has no constraint bits, so nothing shows there. The coarsening also ignores
the flip's result: a criterion hit is counted and its neighbours pushed
whether or not the flip happened. The low three bits of `+0xD`, which the
criterion also tests, are set by the reset (`+0xD = 7`) and by every split
and clone, and only moved by the flip; no other writer was found, so that
test never refusing is STRONGLY INFERRED.

**Coarsening is a bounded flood** (`0x004CC650`). Its generation byte wraps,
clearing the stamp array. It seeds from the four corners of the rectangle it is
given, in the order (x0, y0), (x0, y1), (x1, y0), (x1, y1), onto a stack at
`0x006E6F48`: for each corner `0x004CC3D0` pushes the triangle under it, that
triangle's neighbours in slot order, then the rest of the fan around the
corner's vertex in walk order, each once if live, unstamped and not solid.
It pops a triangle and tries its edges 0, 1, 2 against `0x004CC190`; on a
hit it calls the flip, ignores its result, and goes on to the next edge of
the same, by then rewritten, triangle. After each hit it pushes those of the
triangle's three neighbours that are live, unstamped and not solid (bit 3).
After a hit it **returns early** when the stack depth plus 4 passes 999, so
at about 996 entries; the flip it just made stays. Ours sweeps every touched slot in
order instead; that reaches the same fixed point for a whole-map build
(navigation-mesh-and-search.md §2.1), but nothing has shown that it does for
a flood bounded by a small rectangle. The slot tables the walks use are
`0x0066DA14 = [1, 2, 0]` (next) and `0x0066DA18 = [2, 0, 1]` (previous):
edge k runs from vertex slot k to slot k + 1 and its neighbour is word k.

**What ours does.** `WorldSteering.Refresh` builds the whole map once
(navigation-mesh-and-search.md §2.1: DK2's mesh at level start) and then, for
each routing byte changed since the last refresh, in tile order, runs
`NavMesh.Retriangulate` over that tile: the partial path above, transcribed loop
for loop (ADR-0461). DK2 edits at the moment each byte is written; a refresh
that finds several changed bytes edits them in tile order, not write order. The
keeper AI's `KeeperView` makes its own steering and routes on a fresh build.
After an edit, DK2's procedure, now ours too:

- it keeps every point the edit did not remove;
- it removes redundant side points;
- it reuses slots;
- it coarsens only the rectangle.

That the resulting triangles differ from a fresh build of the same bytes is
STRONGLY INFERRED. A whole-map build keeps every rectangle corner, and the
partial path removes some; but no mesh has been dumped after a dig. That
routes differ is a HYPOTHESIS, since the search and funnel might still choose
the same path. Our comment there, which said whole-map comparison "gives the
same mesh", stated as fact what nothing showed. A DK2 mesh dump after one dig
settles both. Building the partial path faithfully needed, and has (ADR-0461):

- point and triangle free lists;
- vertex removal (`0x004CAF40`, `0x004CB8C0`, `0x004CB170`);
- the side cut and mark;
- the interior walk;
- the flood coarsening, with its seed `0x004CC3D0`;
- the flip's early constraint shuffle;
- a save that carries the mesh, because it now depends on edit history.

Our mesh winds the other way (navigation-mesh-and-search.md §2.1), so each
walk is transcribed through one translation, so that the edit walks the same
triangles in the same order and keeps and frees the same slots from the same
starting triangle. The table (NavMesh.Edit.cs applies it): with a corner `p` at
vertex slot `s` in both meshes, DK2's edge at `s` (leaving `p`) is our `Prev(s)`
(arriving at `p`), its edge `next(s)` is our `Next(s)` and its `prev(s)` our edge
`s`; its corner after `p` is our corner before. Where DK2 enters a triangle
across the edge at slot `b` with `p` at `next(b)`, ours has `p` at `b`: DK2's
edge `next(b)` (from `p` to the apex) is our `Prev(b)`, its edge `prev(b)` our
`Next(b)`, and the apex `prev(b)` is our `Prev(b)` too. DK2's flip keeps the
start of its edge in the first triangle; ours keeps the end. DK2's edges and
neighbours 0, 1, 2 iterate as ours 2, 1, 0. DK2's slot tables are
`0x0066DA14` = [1, 2, 0] (next) and `0x0066DA18` = [2, 0, 1] (prev), overlapping
`0x0066DA10` = [0, 1, 2] by one word, so next(-1) = 0 and prev(-1) = 1. The reviewer's check of
the transcription (2026-10-13) left three places where that start is not DK2's:
DK2's locate (`0x004C98B0`) walks from a 4×4 cell cache (refreshed by
`0x004C97B0`, turning fans by `0x004C9540`) and returns the first containing
triangle it reaches, so at a vertex the fan triangle it names depends on what
was located before, where ours returns the lowest slot holding the point
(ADR-0157), and that triangle starts the degree reduction, labels the degree-4
rim and heads the flood seed's stack (`0x004CC3D0` locates first), so the two
edits can choose different diagonals and flips; the reset (`0x004C8D10`) stores
(p3,p1,p0) and (p1,p3,p2),
a canvas diagonal p1–p3 where ours is p0–p2; and the whole-map builder's corner
order (`0x004CCCB0`: (x0,y0), (x0,y1), (x1,y1), (x1,y0)) and interior split
slots (`0x004CA500`) predate this and are ours. The edit therefore walks DK2's
walks, but from a triangle that may not be DK2's; a DK2 mesh dump after one dig
is still the only check of the result, and none exists.

