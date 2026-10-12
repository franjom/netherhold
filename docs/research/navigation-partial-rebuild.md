# Mechanic: the navigation mesh's per-tile rebuild (Q591)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra on 2026-10-12. Addresses are facts about
  the binary in our own words; no decompiler output enters this repository (ADR-0046)
- [navigation-mesh-and-search.md](navigation-mesh-and-search.md) §2.1 for the whole-map
  build and the insertion, flip and scanner routines this reuses

Original version: DK2 v1.70 (GOG)

Questions: **Q591**. Not built.

---

## 1. What the binary does

Read from `0x004CE770` and its callees on 2026-10-12. Every changed navigation
byte reaches `0x004CE770` with that one tile (`0x004D1930`, navigation-mesh-and-search.md §2), and the mesh
is **edited in place, not rebuilt**. Ours rebuilds the whole map instead
(`WorldSteering.Refresh`); see "What ours does" below for how far that is known
to differ.

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
count `0x006ECA30` drops. The triangle allocator `0x004CA820` takes the head
of that list when it has one, and the next slot past the high-water mark
`0x006ECA2C` only when it is empty. So triangle slots are reused last in,
first out, and after the first edit, slot numbers, and anything ordered by
them, depend on the history of edits and not only on the map. A removed point
goes to its own list through `+0` (head `0x006A11D8`, `+2 = 0x8000`, count
`0x006ECA38`). The pop for that list, in point insertion (`0x004CAA50`,
`0x004CA500`), is unread.

**Coarsening is a bounded flood** (`0x004CC650`). Its generation byte wraps,
clearing the stamp array. It seeds from the four corners of the rectangle it is
given (`0x004CC3D0`, unread) onto a stack at `0x006E6F48`. It pops a triangle
and tries its three edges against `0x004CC190`. After each flip it pushes those
of the flipped triangle's three neighbours that are live, unstamped and not
solid (bit 3). After a flip it **returns early** when the stack depth plus 4
passes 999, so at about 996 entries; the flip it just made stays. Ours
sweeps every touched slot in order instead; that reaches the same fixed point
for a whole-map build (navigation-mesh-and-search.md §2.1), but nothing has shown that it does for a flood
bounded by a small rectangle.

**What ours does.** `WorldSteering.Refresh` compares the whole navigation grid
and, on any difference, builds the mesh from scratch by
navigation-mesh-and-search.md §2.1. That is DK2's mesh at level start. After an
edit, DK2 runs a different procedure:

- it keeps every point the edit did not remove;
- it removes redundant side points;
- it reuses slots;
- it coarsens only the rectangle.

That the resulting triangles differ from a fresh build of the same bytes is
STRONGLY INFERRED. A whole-map build keeps every rectangle corner, and the
partial path removes some; but no mesh has been dumped after a dig, and the
flood seed is unread. That routes differ is a HYPOTHESIS, since the search and
funnel might still choose the same path. Our comment there, which said
whole-map comparison "gives the same mesh", stated as fact what nothing showed.
A DK2 mesh dump after one dig settles both. Ours also rebuilds the whole map
for any change, where DK2's edit is bounded by the tile's rectangle.
Building the partial path faithfully needs:

- point and triangle free lists;
- vertex removal (`0x004CAF40`, `0x004CB8C0`, `0x004CB170`);
- the side cut and mark;
- the interior walk;
- the flood coarsening, with its seed `0x004CC3D0`;
- a save that carries the mesh, because it now depends on edit history.

