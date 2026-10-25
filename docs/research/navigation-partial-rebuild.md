# Mechanic: the navigation mesh's per-tile rebuild (Q591)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra on 2026-10-12. Addresses are facts about
  the binary in our own words; no decompiler output enters this repository (ADR-0046)
- [navigation-mesh-and-search.md](navigation-mesh-and-search.md) §2.1 for the whole-map
  build and the insertion, flip and scanner routines this reuses

Original version: DK2 v1.70 (GOG)

Questions: **Q591**. Built (ADR-0461, ADR-0486) and checked against a Level 10 capture (§5).

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

The same navigation bytes can yield different triangles and routes after an
edit history versus a fresh build: **CONFIRMED by the native controlled rebuild
in §6**. That result does not prove a campaign loss or a defect in our fresh
builder. Building the partial path faithfully needed, and has (ADR-0461):

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
where ours starts each walk was ours (ADR-0461), and the first replay of a DK2
capture (§5) showed it in the result: the locate, the reset diagonal, the corner
order, the split layout and the constraint insertion are since transcribed too
(ADR-0486), and the edit then reproduces DK2's dumps slot for slot.

## 5. Checked against the Level 10 capture

The owner ran the original on Level 10 on 2026-10-15 with `dk2move.py --level
level10 --seconds 300` (reverse-engineering/, gitignored): 141 mesh dumps (one
whenever the triangle table changed, at most one every two seconds), 2,132
navigation-byte changes over 1,110 ticks, and the byte table at the start. The
replay (tools/meshreplay) builds our mesh from the recorded bytes and compares
it with each dump by the triangles' corner coordinates and bytes, and by slot
number; its `step` mode imports DK2's own dump slot for slot (our winding is
DK2's reversed: vertices (v0, v2, v1), neighbours (n2, n1, n0)) and applies the
byte changes logged up to the next dump through `Retriangulate`, so that it
tests the edit alone, from DK2's own starting mesh.

| mesh | before ADR-0486 | after |
|---|---|---|
| whole-map build vs the first dump, triangles | 2,674 of 2,828 | 2,826 of 2,828 |
| same, slot numbers | 96 | 2,823 |
| edit intervals reproduced exactly (`step`) | 106 of 140 | 130 of 140 |
| triangles identical over all 140 intervals | 99.96 % | 99.99 % |
| same slot over all 140 intervals | 99.85 % | 99.98 % |

The ten intervals not exact after ADR-0486 are STRONGLY INFERRED to be the
probe's timing, not the mesh's. Nine differ only in bytes on identical
triangles: of the 18 triangles concerned, 14 carry a byte the log records for
that tile after the dump (a dig or a door toggle the byte thread saw later),
and 4 are door tiles whose byte the log records two ticks before the dump and
the next dump shows again, so the replay's choice of which logged changes a
dump already held is off by one toggle. The tenth has two tiles changed in the
same tick, which the log lists in row order; applied in the other order the
interval is exact, so the original is inferred to have written them that way.
The replay takes the best of three tick limits per interval (the dump's tick
less one, the tick, the tick plus two), which is how a one-tick skew between
the probe's two threads is absorbed; this is disclosed, and it is what "exact"
means here. An ablation isolates the locate: with the old lowest-slot rule and
the new builder the same replay gives 108 of 140 intervals and 2,795 of 2,828
triangles of the first dump (1,737 slots), so the cache walk is what the dumps
confirm, not only the builder. The whole-map build's two remaining triangles
are one rock quad at (42..50, 73..74) with the other diagonal, and three slot
numbers near (28, 12) are permuted; the scanner (`0x004CE570`, `0x004CE4E0`)
was read for this and is ours already, so the cause is some ordering in the
rectangle insertion not yet found. Where the original would loop or read a -1
slot (a walk off the canvas, a constraint that never settles), ours throws or
returns -1; unreachable on this capture, a divergence by construction.

What the first replay showed, before the fixes: a fresh whole-map build of the
bytes at each dump drifts from DK2's edited mesh as play goes on (88.7 % of
triangles over the capture, 84.8 % at the worst dump), where the edited mesh
tracks DK2's triangle count exactly at every dump; and the mismatches that
remained after the locate was transcribed were rock quads next to a dug tile
given another diagonal by the Sloan constraint insertion where DK2's
`0x004CBD10` chose the other, which its transcription closed.

The original's locate cache carries the history of every locate, the route
endpoints' (`0x004C9DF0`) included, which the capture does not log; an edit
after other route queries than ours can still start from another fan triangle.
The replay found no interval that needed another start once the builder was
transcribed, so on this capture the effect did not show; a capture with
`--navlog` would let the route locates be replayed as well.

## 6. Level 1 same-input navigation controls (2026-10-25)

**CONFIRMED, bounded computation.** The three live choices in
[Imp selection evidence](imp-selection-evidence.md) match hash303 jobs/RNG, but
two reconstructed routes differ. The execution-327 choice now separates current
bytes, retained mesh history and fresh building. Modern target remains
`e9663974`, hash303; no simulation code changes or campaign sweep were made.

Evidence root N: `D:/DK2Evidence/level1-navigation-audit-2026-10-25/`.
Inputs are the earlier study's pair02 native before-image, captured navigation,
native mesh and pair03 mesh. New diagnostics reference the archived hash303 DLLs.
All output files are new; the earlier 141-file evidence seal is unchanged.

| Pair02 control | Route matches the live native return? |
|---|---|
| Default reconstructed bytes and fresh modern mesh | No |
| Retained native mesh only, default reconstructed bytes | Yes |
| Captured bytes only, fresh modern mesh | No |
| Retained native mesh and captured bytes | Yes |

Only one reconstructed byte differs: `(14,28)` is 0 instead of the captured 3,
the omitted Heart-builder stamp. Supplying it changes the route, but does not
recover the live route by itself. This omission belongs to fixture reconstruction;
it does not establish that the real modern campaign omitted its builder stamp.

**Native fresh-build control.** `native_cold_mesh.py` calls the actual
`004CE770(0,0,30,38)` in the stopped execution-327 image, then the original
chooser on the resulting mesh. Searches, predicates, router and RNG are not
stubbed. Only the known presentation cache is cleared in emulator memory;
debug logging is already off. Each call has a ten-million-instruction bound.

The retained mesh has 242 live triangles. The native fresh build has 210 and
matches our fresh build from those captured bytes in every live slot: vertices,
neighbours, navigation byte and constraint flags, after the established winding
translation. Its subsequent route exactly matches our captured-byte fresh-build
route, rather than the live retained-mesh route. Both still claim `(14,32)`;
the rebuild consumes no RNG and the chooser ends at seed 335193053 after one draw.
The route discrepancy therefore does not establish a fresh-builder or router
defect: native fresh building itself changes this route.

**Edit-order control.** Pair02 to pair03 changes only two navigation bytes:
`(15,32)` and `(12,19)`, both 8→0. The separate, previously sealed Level 1
full-state playback observes their floor transitions at headers 347 and 350.
Using that order with actual `004D1930` calls on the pair02 native image
reproduces the pair03 captured mesh in all 246 live slots. Reversing the order
to tile order yields 246 live triangles but matches only 234 captured slots.
Our actual `NavMesh.Retriangulate`, given the same starting mesh and each order,
matches its corresponding native result in all 246 slots in both cases.

This confirms order dependence and these two partial-edit transcriptions.
The later actual-history study below establishes batching in another pair;
it does not establish batching of these two native edits or a campaign-loss cause.

Commands (expand N to the evidence root): build `NavigationProbe/ModernProbe.csproj`
and `MeshEditProbe/MeshEditProbe.csproj` with `dotnet build ... -c Release --nologo -m:2`.
The first extends the preserved standalone chooser probe with exclusive mesh/byte
snapshots; its `AUDIT_NAV_PREFIX` selects the new snapshot prefix. The second
runs as `dotnet N/MeshEditProbe/bin/Release/net10.0/MeshEditProbe.dll chronological`
and then `tile`. Native controls: `python -B N/native_cold_mesh.py`, followed by
`python -B N/native_edit_mesh_v2.py --order chronological` and `--order tile`.
The original `native_edit_mesh.py` draft uses the wrong grid receiver and faults
before a byte write; those failed outputs are preserved and excluded.

`python -B N/verify_navigation.py --seal` checked the assertions and sealed 108
artifacts and seven pinned inputs; omit `--seal` for read-only verification.
These controls launch no native process and advance no complete simulation turns.

### Actual update history, hash304

Evidence root: `D:/DK2Evidence/ordered-navigation-hash304-2026-10-25/`.
Pinned tools are the Release build of clean `5716560c`, HashVersion304, archived
under `D:/DK2Evidence/tools/hash304-5716560c-2026-10-25/`. No simulation edits.

A hardware entry/return observer captured 55 complete `004CE770` pairs from one
native `l1live` playback. The 180-second limit expired at execution clock701;
the last rebuild was689. The intended850 boundary was **not reached**. Each pair
retains incoming navigation bytes, triangle/point slots, free chains and the
16-dword locate cache. Replaying the actual area on that incoming state with
our `Retriangulate` matches all55 outgoing meshes, free lists and caches exactly.
The final initialization mesh also matches our first mesh:158 live triangles
and82 live points. This bounds the result to observed calls, not all navigation.

Standalone modern prefixes replay seeds1,3,7 through turn701. An unobserved twin
matches every per-tick state hash; event logs also equal each saved hash303 prefix
(1881/1830/1813 lines). System/event observations force no navigation refresh.
Refresh counters have no jumps above1. Initial navigation bytes match native.
These counters track mesh-changing refreshes, not every refresh invocation;
event/system snapshots can miss transient writes inside a system.

The first two edited tiles agree in order, `(10,28)` then `(10,27)`. The native
third is `(10,29)` at execution76; all three modern seeds edit `(9,28)` third.
Different seeds, clocks and preceding decisions prevent attributing this to the
mesh updater. Native edits rebuild immediately; the first modern write is at
replay44/tick43 and refresh at45/tick44. This is not a universal clock correction.

Seed1 has53 single-tile partial refreshes; seed3 has55. Seed7 has46 single-tile
and two double-tile refreshes. At replay106, events observe `(9,30)` before
`(9,26)`; refresh107 applies `(9,26)` before `(9,30)`. At521, `(17,20)` then
`(15,32)` retain their order at refresh522. Thus real batching and one reversal
are **CONFIRMED** in this prefix. Their effect on the Hatchery deadline or loss
remains **UNKNOWN**; seeds1/3 already diverge without batching in this window.

Commands: `Analysis.dll native <root>/native-events.jsonl <new-results.json>`;
`Analysis.dll modern <1|3|7> 701 <new-output-directory> neutral`, using the
preserved standalone project. These are diagnostic prefixes, not new win rates.
`python -B <root>/verify_ordered_navigation.py` verifies30 sealed artifacts and
the pinned capture inputs. Independent read-only review confirmed the counts,
hash controls and observed reversal, with the observation limit above.
