# Wall support after ownership loss

## 1. Completion callback — CONFIRMED

Primary source: DKII.EXE `0x00455030`, decompiled and its corner loop checked
against assembly on 2026-10-13. Callers include Imp state 20 (`0x00465FE3`),
the possessed enemy-floor action (`0x004978E0`) and room ownership transfer
(`0x004E57BF`). The generic tile-health applier does not perform this cleanup.

After an enemy floor falls to its neutral destroyed terrain, the callback
checks west, north, east and south, in that order. An owned Solid neighbour
without adjacent floor of its own owner becomes its terrain's DestroyedType,
owned by the neutral player. A damaged wall qualifies too; this phase does
not test gold or maximum health. The ordinary terrain setter supplies the
destination's starting health and normal terrain bookkeeping. Shipped
Reinforced Wall (9) becomes Rock (2).

Support is `0x00453B30`: an orthogonal neighbour with the wall's owner,
Ownable terrain and no Solid flag. Another keeper's floor does not support
the wall. Rooms and bridges can support it. Thus capturing one floor removes
only walls that lose their last qualifying supporting floor.

Room transfer calls the same callback after each slab changes owner, in the
existing slab-chain order. Mutations are immediate, so later checks observe
earlier wall conversions.

## 2. Corners — CONFIRMED

The four candidate offsets in the signed-dword table at `0x00697A30` are
northwest, northeast, southeast and southwest. Each candidate must be owned,
Solid, contain no gold and have exactly its terrain's maximum health. It must
also have no adjacent floor of its own owner.

For each candidate, four pairs of neighbouring offsets are tested:
`(north, west)`, `(north, east)`, `(west, north)`, `(west, south)`.
The reversed duplicate is present in the shipped table. A pair preserves the
candidate when both members are owned, Solid, nongold and at maximum health.
Their owners need not match each other or the candidate. With no qualifying
pair, the candidate becomes neutral DestroyedType. These restrictions apply
only to the diagonal phase, not the four direct neighbours.

The reimplementation checks map bounds; original campaign borders keep the
native unchecked neighbour accesses in range.

## 3. Scope of the supporting binary trace

The 2026-10-13 trace also inspected `0x00454B80`, `0x00454E00`,
`0x00452340`, `0x00450770`, `0x0044F370`, `0x004ECDB0` and `0x00452D80`.
Their individual records state the bounded findings and remaining uncertainty.
These local flag, room, reset and surface routines are distinct from the
ownership-completion callback; no decompiler text is retained here.
