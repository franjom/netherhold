# Doors and traps: verbatim placed state

This note records the data-side evidence needed to stop discarding placed door
and trap records. No binary was read for this work, so it establishes storage
only and makes no claim about behaviour.

## Result in one sentence

The shipped corpus contains 940 doors and 959 traps across 48 levels; every
parsed field now enters ordered, hashed and snapshotted world state verbatim,
while opening, closing, locking, blocking, arming, firing and shot consumption
remain UNKNOWN.

## Shipped records

`KwdAdditionalRecords` parses the complete payloads below, and the existing
record round-trip keeps the original bytes. `PlacedThingsOnRealDataTests` walks
every shipped THINGS file and compares each projected state back to its source.

| record | fields stored | corpus finding | confidence |
|---|---|---|---|
| door | `X`, `Y`, `Unknown_0008`, `TriggerId`, `DoorId`, `PlayerId`, `Flags`, `Unknown_0011` | 940 records; every `TriggerId` is zero | **CONFIRMED from shipped data** |
| trap | `X`, `Y`, `Unknown_0008`, `NumberOfShots`, `TrapId`, `PlayerId`, `Unknown_000F` | 959 records | **CONFIRMED from shipped data** |

The records occur on 48 of the 66 levels with THINGS files. Level 1 contains
neither type. Door `TriggerId` is zero in all 940 records, so doors add no new
trigger root and `dk2tools kwd roots` remains complete with world, party,
player, creature and action-point roots.

## World-state decision

`WorldState.Doors` and `WorldState.Traps` preserve their THINGS-file order.
Projection copies every parsed value without masking, filtering, scaling,
coordinate conversion or validation. The three bytes in `Unknown_0011` are
copied into world-owned storage so later mutation of a parser buffer cannot
change simulation state outside the world.

Every field is hashed and snapshotted. This is deliberately conservative: it
loses no source information. The remaining risk is that DK2 discards or
transforms a field before runtime, in which case the simulation hashes a bit or
value DK2 does not. Q250–Q253 name those unknowns; resolving one requires the
binary and may narrow the stored representation later.

Adding the two collections changes the state shape even in an empty world, so
`HashVersion` and snapshot `FormatVersion` are 45. The version-45 determinism
pin is recorded in `hash-versions.md`.

## No behaviour

Doors do not open, close, lock or block movement. Traps do not arm, fire or
consume shots. The state makes those mechanics possible to research and add
later; it does not guess them. Q249 owns that behavioural work.

## Confidence summary

| claim | confidence |
|---|---|
| Counts, level count, field values and all-zero door trigger ids | **CONFIRMED from shipped data** |
| Projection preserves every parsed field and per-type file order | **CONFIRMED by tests** |
| Any field's runtime meaning or transformation | **UNKNOWN** |
| Door and trap behaviour | **UNKNOWN** |
