# Action points: from the THINGS file into runtime state

This note answers the action-point storage questions needed before a level's
`ActionPointThing` records could enter `WorldState`. The primary source is the
GOG 1.70 `DKII.EXE`; addresses below are navigation anchors, and every binary
finding is paraphrased rather than copied from decompiler output.

The shipped corpus contains **1,152 action points**. Campaign level 1 contains
27. `dk2tools kwd things` is the data-side reproduction path.

## Result in one sentence

An action point is a rectangular, id-addressed level object with a static
trigger and waypoint link, five loader-selected flag bits, and two saved runtime
words; it has **no `Available` field**, and trigger actions 16, 17, 27 and 31 do
not mutate one.

## Runtime layout and load

The constructor at `0x004B0420`, level loader at `0x005211A0`, diagnostic field
walker at `0x004B0B90`, and save/load pair at `0x004B0460` / `0x004B0550`
agree on this relevant tail of the runtime object:

| offset | width | model | initial source | confidence |
|---:|---:|---|---|---|
| `+0x2E` | 2 | passage counter | zero | **STRONGLY INFERRED** storage/name/initial value; consumer UNKNOWN |
| `+0x30` | 2 | wait timer | `WaitDelay × variable 19` | **STRONGLY INFERRED** |
| `+0x32` | 1 | flags | selected source flag bits | **STRONGLY INFERRED** |
| `+0x33` | 1 | next waypoint id | `NextWaypointId` | **STRONGLY INFERRED** |
| `+0x34` | 2 | trigger root id | `TriggerId` | **STRONGLY INFERRED** |
| `+0x36` | 1 | trigger repeat count | resolved trigger record | **STRONGLY INFERRED**, not yet projected because THINGS projection has no trigger records |
| `+0x37` | 1 | action-point id | `ActionPointId` | **STRONGLY INFERRED** |
| `+0x38..+0x3F` | 4 × 2 | rectangle corners, in the order **StartX `+0x38`, StartY `+0x3A`, EndX `+0x3C`, EndY `+0x3E`** | low words of four source dwords | **CONFIRMED** — the order is read off action 17's walk, which indexes the slab array with the `+0x38`/`+0x3C` pair as the column and multiplies the `+0x3A`/`+0x3E` pair by the map width (`trigger-dispatch-tables.md`) |

Variable 19 is the level's original tick rate and is 4 in all shipped levels.
The loader multiplies the authored `WaitDelay` once and stores the low 16 bits.
`ActionPointState.WaitTimerTicks` reproduces that boundary. The runtime save
path persists the whole nine-byte block from `+0x2E` through `+0x36`, so the
counter and timer are world state even though their gameplay writers/readers
remain unresolved (Q236).

The name string is not in this runtime tail and is not simulation state. It is
therefore not hashed or snapshotted.

## Flags: five loaded bits, not availability

The source is a 16-bit word, but the loader examines its low byte and calls five
setters. Those setters only toggle the corresponding bit in runtime byte
`+0x32`.

| source bit | loaded? | direct runtime reader found | conclusion |
|---:|---|---|---|
| 0 | yes | `0x0046122C` | a creature routine may use the point's top-left tile; exact purpose **UNKNOWN** |
| 1 | no | none; absent from shipped corpus | not runtime state |
| 2 | yes | none | opaque live-format bit; purpose **UNKNOWN** |
| 3 | yes | none | opaque live-format bit; purpose **UNKNOWN** |
| 4 | yes | `0x0050C083` | drives an inclusive-rectangle tile/area update; exact effect is not named here |
| 5 | yes | `0x00510E36` | a world lookup can return the id of a matching point; lookup purpose **UNKNOWN** |
| 6 | **no** | none | occurs in shipped data but is discarded by the loader |
| 7 | no | none | absent from shipped corpus |

Thus the faithful runtime mask is `0x3D`. In particular, bit 2 is **not** an
initial `Available` value. The full initialized-block scan found no gameplay
reader for bits 2 or 3, but that negative cannot rule out a computed access;
Q235 keeps their meanings open.

## The four actions do not write an action point

The earlier repo claim came from four parameter views whose second or third
byte was transcribed as `Available`. Reading their complete dispatch paths
refutes the shared interpretation:

- **Action 16** controls the world's four tutorial-highlight rectangles, without
  changing the action point. Built; see [scripted red flash](tag-highlight.md#scripted-tutorial-red-flash).
- **Action 17** resolves the point and chooses between two operations over its
  inclusive rectangle. It does not write the point. Both operations are now
  read: they set and clear **one bit of each slab's flag word**, and nothing
  found reads that bit except the overhead map
  (`trigger-dispatch-tables.md`, "Action type 17").
- **Action 27**, player dispatcher `0x004C8620`, takes the rectangle centre and
  constructs a separate camera/event command. `Angle` becomes that command's
  angle and `Time` is converted to milliseconds. Neither is action-point data.
- **Action 31** constructs a separate path command centred on the point. Its
  third byte conditionally sets the high bit of the command's path id. It does
  not change `NextWaypointId` or any other point field.

The parameter names still describe bytes read by their individual handlers;
what was false was promoting the repeated word `Available` into one shared
action-point property. ADR-0102's contrary consequence is corrected by
ADR-0103.

## Rectangle and cost

The rectangle dispatcher at `0x004B0D70` and helpers at `0x004B0620`,
`0x004B06D0`, `0x004B07E0`, `0x004B0960` and `0x004B0A00` iterate from start
through end on both axes **inclusively**. Depending on the operation they visit
tiles or objects within those tiles. The bit-4 world reader has the same
inclusive shape.

It is therefore **STRONGLY INFERRED** that relevant work can scale with rectangle area
and contents, and Q237's remaining half is answered: the rectangle cost is the
**condition evaluation itself**. Every one of the seven types
`0x004B0D70` owns sweeps the rectangle once, and the tick that reaches it is
vtable slot `0x14`, `0x004B05E0` — five instructions, walk the trigger root at
`+0x34` with the repeat at `+0x36`, nothing else. So a point with no root costs
nothing at all and a point with one costs its rectangle per condition per turn.

`+0x36` is written only by `0x004B0C90`, which copies the resolved root record's
own repeat byte when the root is attached. The repeat is therefore a load-time
snapshot rather than file data or a live read; `trigger-walkers.md` sets it
against the world's and the player's.

## Waypoints

`NextWaypointId` is copied at load, shown by the diagnostic path, and preserved
by save/load. A complete direct-offset scan found no post-load writer. Action
31 does not relink it. **Static directed link** is therefore strongly supported;
the exact creature traversal rule, termination rule, and handling of missing or
cyclic ids remain **UNKNOWN** (Q238). Of the 1,152 shipped points, 475 carry a
non-zero link.

## World-state decision

`WorldState.ActionPoints` preserves THINGS-file order. Each state hashes and
snapshots:

- id and inclusive rectangle;
- trigger id and next-waypoint id;
- the loader-filtered flag byte;
- passage counter and wait timer.

File order matters because several original lookups walk the collection. No
sorting, deduplication, or rectangle validation is introduced. Trigger repeat
count will be added only when trigger records are projected, because inventing
it without the record the original resolves would be worse than leaving the
known dependency explicit.

Action points are hashed and snapshotted, so landing them bumped both
`HashVersion` and the snapshot `FormatVersion` and recomputed the pin. The
current values are the constants in `GameSimulation` and `WorldSnapshot`; the
history is in `hash-versions.md`.

## Confidence summary

| claim | confidence |
|---|---|
| Runtime offsets, widths, initialization and saved block | **STRONGLY INFERRED** from the binary |
| No runtime `Available` field; actions 16/17/27/31 do not write the point | **STRONGLY INFERRED** from complete binary paths |
| Action 27 angle/time belong to its camera/event command | **STRONGLY INFERRED** from the binary |
| Loader flag mask is `0x3D`; source bit 6 is discarded | **STRONGLY INFERRED** from the binary; source values CONFIRMED from shipped data |
| Rectangle iteration is inclusive and may scale with area | **STRONGLY INFERRED** from the binary |
| Next waypoint is static after load | **STRONGLY INFERRED** from exhaustive direct references |
| Meanings of opaque flag bits and the timer/counter consumers | **UNKNOWN** |
| Exact action-point update cadence and entry transition | **UNKNOWN** |
