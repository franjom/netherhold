# Hero parties and effect generators: verbatim placed state

This note records the data-side evidence needed to stop discarding the last two
parsed THINGS record types. No binary was read for this work. It establishes
storage only and makes no claim about spawning or effect behaviour.

## Result in one sentence

The 48-level campaign contains 220 hero parties, and the shipped corpus contains
one effect generator; every parsed field now enters ordered, hashed and
snapshotted world state verbatim, while spawning and generation remain UNKNOWN.
Across the complete 66-level shipped sweep, 35 levels contain a party.

## Record shapes

| record | fields stored | confidence |
|---|---|---|
| hero party | `Name`, `TriggerId`, `PartyId`, `Unknown_0023`, `Unknown_0027`, `Members` | **CONFIRMED from parser and shipped data** |
| party member | all `GoodCreatureThing` fields, including the two-byte `Unknown_001D` tail | **CONFIRMED from parser and tests** |
| effect generator | `X`, `Y`, four scalar unknowns, variable `EffectIds`, `Frequency`, `GeneratorId`, six-byte `Unknown_001E` | **CONFIRMED from parser and tests** |

Projection copies values without coordinate conversion, validation, flag masks
or scaling. Arrays are copied into world-owned storage so parser-side mutation
cannot alter state. List counts enter both hash and snapshot. Fixed tails are
written without lengths, so their state setters enforce the parser's two- and
six-byte widths; accepting another width would desynchronise every later field.

## Party members are templates

A party has sixteen `GoodCreatureThing` slots in shipped records. They describe
an unspawned party, not creatures standing in the level. Projection stores those
records under `HeroPartyState.Members`; it does not call `SpawnCreature`, and the
projection's creature count remains unchanged. Action type 14 is the route that
would spawn a party, but its behaviour is not implemented (Q254).

`TriggerTree` still independently builds its 24 reduced party slots from the
source `HeroPartyThing` sequence. The same records therefore have two homes for
now. Refactoring that ownership without the spawn path would be speculation, so
Q256 records it rather than this change rewiring the tree.

## No behaviour

Parties do not spawn. Effect generators do not generate or choose effects. Field
meanings, transformations, cadence and consumption are UNKNOWN (Q254–Q259).
Verbatim storage loses no information; its conservative risk is hashing a value
or bit DK2 discards or transforms.

Adding both collections changes the state shape even in an empty world, so
`HashVersion` and snapshot `FormatVersion` are 47. The pin is recorded in
`hash-versions.md`.

## Corpus gate

`PlacedThingsOnRealDataTests` walks all shipped levels, compares every projected
party, member and generator field with its parsed source, asserts 220 parties,
one generator and zero `NotYetProjected`, and proves party members do not inflate
the placed-creature count.
