# Wall corner fill

## Finishing state 22 (Q694)

STRONGLY INFERRED from the complete disassembly/decompilation of `0x00466390`
through its return, `0x00453CC0`, `0x00453F70`, `0x00509200` and the positive
arm of `0x00452F60` (2026-10-31). No OpenKeeper code was used.

State 22 waits for its action and requires its kind-6 task still to exist.
If the neutral-wall continuation fails, it tests the worked tile as finished:
owner differs from the neutral global, terrain Solid, zero gold value, and
health exactly terrain MaxHealth. Here neutral is owner 2; owner 0 is distinct.
Neither Ownable nor keeper identity nor Impenetrable is checked on a
finished tile.

The 96 bytes at `0x00698A00`, read as signed dwords, contain four triples:

| diagonal | first shared side | second shared side |
|---|---|---|
| (-1,-1) | (0,-1) | (-1,0) |
| (1,-1) | (0,-1) | (1,0) |
| (1,1) | (1,0) | (0,1) |
| (-1,1) | (0,1) | (-1,0) |

For each finished diagonal, first side then second side is tested. A side
qualifies when neutral, Solid, penetrable, goldless, and health differs from
MaxHealth. The opposite side must have navigation bit 3 clear, through world
slot +0x208 (`0x00509200`); ownership and ordinary floor flags are irrelevant.
The health delta passed through `0x004508D0` is the candidate terrain's full
MaxHealth, added to its current health and clamped to 32000. The applier
converts to MaxHealthType and assigns the worker's owner on conversion. If that type is unchanged, it
only clamps health: a neutral reinforced wall stays neutral. Rock becomes
reinforced terrain at its destination's StartingHealth, 900 in shipped data.
The loop does not recursively finish newly filled corners.

CallScanAll across .text and cseg finds state 22 only in its dispatch-table
installation at `0x0047C18C`. `0x00453F70` has only its two state-22 calls.
`0x00453CC0` also has callers at `0x00472080`, `0x004869F1`, `0x00486A8F`,
`0x00486B86`, `0x00486C2C`; these are predicate users, not additional callers
of this fill loop. StoreAt for tile offsets +6/+8/+A confirms the applier's
health writes at `0x00452FB1/BB/DF/E9`; the tail itself writes no tile fields.

Ours runs the loop when a reinforcer's action ends and its completed job is
released, excluding cancellation by a dig mark. Raw health completion reuses
terrain conversion and completion effects. No new persistent fields.

## Built scope and replay limits

Built at hash/save 347 (ADR-0605) in `ReinforcementRules`,
`ImpTaskSystem.JobStillStands` and `TileWorkRules.CompleteWall`. State 22
continues an eligible neutral wall even when it is not Ownable, and only
reaches the corner tail after that continuation fails. Focused controls cover
the completed non-Ownable target, exact owner tags, corner predicates,
same-type healing and terrain conversion.

A read-only count from
`D:\DK2Evidence\dk2runs\level4-live1\seed01\tiles.csv` has 164 terrain-9
tiles initially and 395 at tick 6804; `level4.dk2cap` is adjacent. The modern
seed-1 replay has 363 at the same endpoint (`build/unread-q694-tiles.csv`;
command log `build/unread-q694-replay.log`). It reused
`D:\DK2Evidence\runs\packet-replay-hash328\level4\seed01\entity-map.csv`
without revalidating its remapping. This is a one-seed wall-count observation,
not a matched transition comparison or native parity result.
